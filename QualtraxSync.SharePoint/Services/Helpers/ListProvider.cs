using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using QualtraxSync.SharePoint.Client;
using QualtraxSync.SharePoint.Configuration.Helpers;
using QualtraxSync.Contracts.Services;
using System.Collections.Concurrent;
using System.Reflection;

namespace QualtraxSync.SharePoint.Services.Helpers;


public class ListProvider<T>(
    IntegrationGraphClient graphClient,
    IListService listService,
    IServiceScopeFactory serviceScopeFactory,
    SiteSchema<T> schema,
    ILogger<ListProvider<T>> logger) where T : class
{
    protected readonly IntegrationGraphClient Graph = graphClient;

    private readonly ConcurrentDictionary<object, ListItem> _lookupKeyCache = new();
    private readonly ConcurrentDictionary<string, ListItem> _lookupIdCache = new();

    public async Task<ListKey> EnsureLookupListAsync(string siteId)
    {
        var listName = schema.Name;
        var list = await FindListAsync(siteId, listName);

        if (list == null)
        {
            var newList = new List
            {
                DisplayName = listName,
                ListProp = new ListInfo { Template = "genericList" }
            };

            list = await Graph.Sites[siteId].Lists.PostAsync(newList) ?? throw new InvalidOperationException($"Failed to create lookup list '{listName}'.");

            logger.LogDebug("Created lookup list '{ListName}'.", listName);
        }

        await EnsureLookupListColumnsAsync(siteId, list.Id!);

        logger.LogDebug("List '{ListName}' schema confirmed.", listName);

        using var dataScope = serviceScopeFactory.CreateScope();

        await UpdateAsync(siteId, false, []);

        return new ListKey(list.Id!, schema.LookupField.GetMappedName(siteId));
    }

    public T GetLookupValue(string siteId, string itemId)
    {
        if (_lookupKeyCache.Values.FirstOrDefault(item => item.Id == itemId) is ListItem cachedItem && cachedItem.Fields?.AdditionalData != null)
        {
            return cachedItem.Fields.AdditionalData.GetMetadata<T>(listService, siteId);
        }

        throw new Exception($"No lookup value found for ID '{itemId}' in site '{siteId}'. Ensure the lookup list is properly configured and contains the expected items.");
    }

    public async Task<string> GetLookupIdAsync(string siteId, T value) => (await GetLookupAsync(siteId, value, 0))?.Id ?? throw new Exception($"No ID found for {value} in site '{siteId}'.");

    private async Task<ListItem> GetLookupAsync(string siteId, T value, int tries = 0)
    {
        var listName = schema.Name;
        var lookupFieldValue = GetLookupFieldValue(value);

        if (_lookupKeyCache.TryGetValue(lookupFieldValue, out var listItem))
        {
            return listItem;
        }

        if (tries >= 2)
        {
            throw new InvalidOperationException($"Failed to retrieve lookup ID for item '{lookupFieldValue}' in list '{listName}' after multiple attempts. Ensure the lookup list is properly configured and contains the expected items.");
        }

        if (tries == 1)
        {
            logger.LogDebug("Lookup item '{Title}' not found in Azure. Adding to list '{ListName}'.", schema.LookupField.Name, listName);

            await UpdateAsync(siteId, false, value);
        }

        if (tries == 0)
        {
            logger.LogDebug("Lookup item '{Title}' not found in cache for list '{ListName}'. Reloading cache from Azure.", schema.LookupField.Name, listName);
        }

        var list = await FindListAsync(siteId, listName);
        var listId = list?.Id ?? throw new InvalidOperationException($"Lookup list '{listName}' not found. Ensure the list exists and is properly configured.");

        foreach (var item in await GetLookupListItems(siteId, listId))
        {
            _lookupKeyCache[item.Key] = item.Value;

            if (item.Value.Id != null)
            {
                _lookupIdCache[item.Value.Id] = item.Value;
            }
        }

        return await GetLookupAsync(siteId, value, ++tries);
    }

    private object GetLookupFieldValue(T value)
    {
        var lookupField = schema.LookupField;

        return lookupField.GetValue(value) ?? throw new InvalidOperationException($"Lookup field '{lookupField.Name}' item cannot be null for type '{typeof(T).Name}'.");
    }

    public async Task UpdateAsync(string siteId, bool removeIfMissing, params T[] items)
    {
        var listName = schema.Name;

        logger.LogDebug("Updating lookup list for '{ListName}' with {Count} items. Remove if missing: {RemoveIfMissing}.", listName, items.Length, removeIfMissing);

        if (await FindListAsync(siteId, listName) is not List list)
        {
            var ensured = await EnsureLookupListAsync(siteId);

            list = await FindListAsync(siteId, listName) ?? throw new InvalidOperationException($"Failed to find or create lookup list '{listName}'.");
        }

        var listId = list.Id!;

        var lookupFieldName = schema.LookupField.GetMappedName(siteId);
        var existingLookups = await GetLookupListItems(siteId, listId);
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var additions = new BatchRequestContentCollection(Graph);
        foreach (var item in items.Where(item => !existingLookups.Remove(GetLookupFieldValue(item))))
        {
            var newItem = new ListItem
            {
                Fields = new FieldValueSet
                {
                    AdditionalData = await item.GetFieldValuesAsync(siteId, listService)
                }
            };

            // add new items that are in the list of values given but not in existing items
            var request = Graph.Sites[siteId].Lists[listId].Items.ToPostRequestInformation(newItem);
            await additions.AddBatchRequestStepAsync(request);
        }

        if (additions.BatchRequestSteps.Any())
        {
            logger.LogDebug("Adding {Count} new items to lookup list '{ListName}'.", additions.BatchRequestSteps.Count, listName);

            await Graph.Batch.PostAsync(additions);
        }

        var deletions = new BatchRequestContentCollection(Graph);
        if (removeIfMissing && existingLookups.Count > 0)
        {
            foreach (var item in existingLookups)
            {
                // remove existing items that were not in the list of values given
                var request = Graph.Sites[siteId].Lists[listId].Items[item.Value.Id!].ToDeleteRequestInformation();
                await deletions.AddBatchRequestStepAsync(request);
            }

            logger.LogDebug("Removing {Count} items from lookup list '{ListName}' that are not in the provided items.", existingLookups.Count, listName);

            await Graph.Batch.PostAsync(deletions);
        }

        if (additions.BatchRequestSteps.Count > 0 || deletions.BatchRequestSteps.Count > 0)
        {
            // invalidate the cache for this lookup list since items have been added or removed
            _lookupKeyCache.Clear();
            _lookupIdCache.Clear();
        }

        if (_lookupKeyCache.IsEmpty || _lookupIdCache.IsEmpty)
        {
            // if the cache is empty, populate it with the current items from the list
            foreach (var item in await GetLookupListItems(siteId, listId))
            {
                _lookupKeyCache[item.Key] = item.Value;
                if (item.Value.Id != null)
                {
                    _lookupIdCache[item.Value.Id] = item.Value;
                }
            }
        }
    }

    private async Task<Dictionary<string, ColumnDefinition>> GetLookupListColumnsAsync(string siteId, string listId)
    {
        var columns = new Dictionary<string, ColumnDefinition>();
        var response = await Graph.Sites[siteId].Lists[listId].Columns.GetAsync();
        if (response != null)
        {
            var pager = PageIterator<ColumnDefinition, ColumnDefinitionCollectionResponse>.CreatePageIterator(
                Graph,
                response,
                column =>
                {
                    if (column.Name != null)
                    {
                        columns[column.Name] = column;
                    }
                    return true;
                });
            await pager.IterateAsync();
        }
        return columns;
    }

    private async Task EnsureLookupListColumnsAsync(string siteId, string listId)
    {
        var columns = await GetLookupListColumnsAsync(siteId, listId);
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var name = prop.GetMappedName(siteId);
            if (!columns.ContainsKey(name))
            {
                var siteColumns = await Graph.Sites[siteId].Columns.GetAsync(r =>
                {
                    r.QueryParameters.Filter = $"name eq '{name}'";
                });

                var column = siteColumns?.Value?.Where(c => c.Id != null && c.Name == name).FirstOrDefault();
                if (column == null)
                {
                    column = prop.CreateColumnDefinition(siteId, nameof(QualtraxSync), listService);
                    column = await Graph.Sites[siteId].Columns.PostAsync(column) ?? throw new Exception($"Failed to create column {name} for list {listId}");
                }

                var create = new ColumnDefinition
                {
                    Id = column.Id
                };
                var created = await Graph.Sites[siteId].Lists[listId].Columns.PostAsync(create) ?? throw new Exception($"Failed to bind column {name} to list {listId}");
                var required = new NullabilityInfoContext().Create(prop).ReadState == NullabilityState.NotNull;
                var update = new ColumnDefinition
                {
                    Hidden = false,
                    Required = required,
                    EnforceUniqueValues = prop.Name == schema.LookupField.Name
                };
                await Graph.Sites[siteId].Lists[listId].Columns[created.Id].PatchAsync(update);
            }
        }
    }

    private async Task<Dictionary<object, ListItem>> GetLookupListItems(string siteId, string listId)
    {
        var items = new Dictionary<object, ListItem>();
        var lookupField = schema.LookupField.GetMappedName(siteId);
        var response = await Graph.Sites[siteId].Lists[listId].Items.GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Expand = ["fields"];
        });
        if (response != null)
        {
            var pager = PageIterator<ListItem, ListItemCollectionResponse>.CreatePageIterator(
                Graph,
                response,
                item =>
                {
                    if (item.Id != null && item.Fields?.AdditionalData?[lookupField]?.ToString() is object value)
                    {
                        items[value] = item;
                    }
                    return true;
                });
            await pager.IterateAsync();
        }
        return items;
    }

    private async Task<List?> FindListAsync(string siteId, string listName)
    {
        var lists = await Graph.Sites[siteId].Lists.GetAsync(r =>
        {
            r.QueryParameters.Filter = $"displayName eq '{listName}'";
        });

        return lists?.Value?.FirstOrDefault();
    }
}