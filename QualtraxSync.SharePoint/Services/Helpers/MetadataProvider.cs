using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using QualtraxSync.Contracts.Services;
using QualtraxSync.SharePoint.Client;
using QualtraxSync.SharePoint.Configuration.Helpers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace QualtraxSync.SharePoint.Services.Helpers;

/// <summary>
/// Applies and retrieves content type metadata of type <typeparamref name="T"/> for drive items in SharePoint.
/// </summary>
public class MetadataProvider<T>(
    IntegrationGraphClient graphClient,
    IListService listService,
    SiteSchema<T> schema,
    SiteIdProvider siteIdProvider,
    ILogger<MetadataProvider<T>> logger) where T : class
{
    protected readonly IntegrationGraphClient Graph = graphClient;
    private readonly ConcurrentDictionary<string, ContentTypeKey> _contentTypes = new();

    private string ContentTypeName => schema.Name;

    /// <summary>
    /// Ensures the content type for <typeparamref name="T"/> exists on the site, is bound to the document
    /// library and contains a column for every property of <typeparamref name="T"/>.
    /// </summary>
    public async Task<ContentTypeKey> ProvisionAsync(string driveId, CancellationToken cancellation)
    {
        if (_contentTypes.TryGetValue(driveId, out var cached)) return cached;

        var siteId = await siteIdProvider.ResolveSiteIdAsync(driveId);
        var contentType = await FindContentTypeAsync(siteId, ContentTypeName);

        if (contentType == null)
        {
            var newContentType = new ContentType
            {
                Name = ContentTypeName,
                Description = schema.Description ?? string.Empty,
                Base = new ContentType { Id = GetBaseContentTypeId() }
            };

            contentType = await Graph.Sites[siteId].ContentTypes.PostAsync(newContentType, null, cancellation)
                ?? throw new InvalidOperationException($"Failed to create content type '{ContentTypeName}'.");

            logger.LogDebug("Created content type '{ContentTypeName}'.", ContentTypeName);
        }

        await EnsureContentTypeColumnsAsync(siteId, contentType.Id!, cancellation);

        var listId = await GetListIdAsync(driveId, 0, cancellation);

        var listContentType = await EnsureListContentTypeAsync(siteId, listId, contentType.Id!, 0, cancellation);

        logger.LogDebug("Content type '{ContentTypeName}' schema confirmed.", ContentTypeName);

        var key = new ContentTypeKey(listId, listContentType);

        _contentTypes[driveId] = key;

        return key;

    }

    /// <summary>
    /// Applies the given metadata to the drive item, updating its content type when necessary.
    /// </summary>
    public async Task UpdateAsync(string driveId, string path, T metadata, CancellationToken cancellation)
    {
        var key = await ProvisionAsync(driveId, cancellation);
        var siteId = await siteIdProvider.ResolveSiteIdAsync(driveId);
        var listItemId = await GetListItemIdAsync(driveId, path);

        var update = new ListItem
        {
            ContentType = new ContentTypeInfo { Id = key.ContentTypeId },
            Fields = new FieldValueSet
            {
                AdditionalData = await metadata.GetFieldValuesAsync(siteId, listService)
            }
        };

        DateTimeOffset modifiedDate = default;
        DateTimeOffset createdDate = default;

        if (update.Fields.AdditionalData.TryGetValue("Modified", out var modified) &&
            DateTimeOffset.TryParse(modified?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out modifiedDate))
        {
            update.Fields.AdditionalData.Remove("Modified");
        }

        if (update.Fields.AdditionalData.TryGetValue("Created", out var created) &&
            DateTimeOffset.TryParse(created?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out createdDate))
        {
            update.Fields.AdditionalData.Remove("Created");
        }

        var updated = await Graph.Sites[siteId].Lists[key.ListId].Items[listItemId].PatchAsync(update, cancellationToken: cancellation);

        if (modifiedDate != default || createdDate != default)
        {
            var updateDriveItem = new DriveItem
            {
                FileSystemInfo = new Microsoft.Graph.Models.FileSystemInfo
                {
                    LastModifiedDateTime = modifiedDate == default ? updated?.LastModifiedDateTime : modifiedDate,
                    CreatedDateTime = createdDate == default ? updated?.CreatedDateTime : createdDate,
                }
            };

            await Graph.Drives[driveId].Root.ItemWithPath(path).PatchAsync(updateDriveItem, cancellationToken: cancellation);
        }
    }

    /// <summary>
    /// Reads the field values of the drive item and maps them to <typeparamref name="T"/>.
    /// </summary>
    public async Task<T> GetAsync(string driveId, string path, CancellationToken cancellation)
    {
        await ProvisionAsync(driveId, cancellation);

        var siteId = await siteIdProvider.ResolveSiteIdAsync(driveId);

        var listItem = await Graph.Drives[driveId].Root.ItemWithPath(path).ListItem.GetAsync(request =>
        {
            request.QueryParameters.Expand = ["fields"];
        }, cancellation) ?? throw new InvalidOperationException($"No list item found for drive item '{path}'.");

        var fields = listItem.Fields?.AdditionalData
            ?? throw new InvalidOperationException($"No field values found for drive item '{path}'.");

        return fields.GetMetadata<T>(listService, siteId);
    }

    /// <summary>
    /// Gets every item in the document library that uses the content type for <typeparamref name="T"/>.
    /// </summary>
    public async Task<Dictionary<string,T>> GetAllAsync(string driveId, CancellationToken cancellation)
    {
        var key = await ProvisionAsync(driveId, cancellation);
        var siteId = await siteIdProvider.ResolveSiteIdAsync(driveId);
        var results = new Dictionary<string,T>();

        var response = await Graph.Sites[siteId].Lists[key.ListId].Items.GetAsync(config =>
        {
            config.QueryParameters.Filter = $"fields/ContentType eq '{ContentTypeName}'";
            config.QueryParameters.Expand = ["fields"];
            config.Headers.Add("Prefer", "HonorNonIndexedQueriesWarningMayFailRandomly");
        }, cancellation);

        if (response != null)
        {
            var pager = PageIterator<ListItem, ListItemCollectionResponse>.CreatePageIterator(
                Graph,
                response,
                item =>
                {
                    if (item.ContentType?.Id == key.ContentTypeId)
                    {
                        if (item.Fields?.AdditionalData is { } fields && 
                            Uri.TryCreate(item.WebUrl, UriKind.Absolute, out var webUrl)
                            && GetMetadata(fields, siteId) is { } metadata)
                        {
                            var fullPath = Uri.UnescapeDataString(webUrl.AbsolutePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
                            results[string.Join("/", fullPath.Skip(3))] = metadata;
                        }
                    }

                    return true;
                });

            await pager.IterateAsync(cancellation);
        }

        return results;
    }

    private T? GetMetadata(IDictionary<string, object> fields, string siteId)
    {
        try
        {
            return fields.GetMetadata<T>(listService, siteId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize field values into {TypeName}: {Fields}", typeof(T).Name, fields);
            return default;
        }
    }

    private async Task EnsureContentTypeColumnsAsync(string siteId, string contentTypeId, CancellationToken cancellation)
    {
        var columns = await GetColumnsAsync(siteId, contentTypeId, cancellation);

        Dictionary<string, ColumnDefinition>? siteColumns = null;

        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = property.GetMappedName(siteId);
            if (columns.ContainsKey(name)) continue;

            siteColumns ??= await GetColumnsAsync(siteId, null, cancellation);

            if (siteColumns.TryGetValue(name, out var column) == false)
            {
                column = property.CreateColumnDefinition(siteId, nameof(QualtraxSync), listService);
                column = await Graph.Sites[siteId].Columns.PostAsync(column, null, cancellation)
                    ?? throw new InvalidOperationException($"Failed to create column '{name}' for content type '{ContentTypeName}'.");
            }

            var link = new ColumnDefinition
            {
                AdditionalData = new Dictionary<string, object>
                {
                    { "sourceColumn@odata.bind", $"https://graph.microsoft.com/v1.0/sites/{siteId}/columns/{column.Id}" }
                }
            };

            await Graph.Sites[siteId].ContentTypes[contentTypeId].Columns.PostAsync(link, null, cancellation);
        }
    }

    private async Task<Dictionary<string, ColumnDefinition>> GetColumnsAsync(string siteId, string? contentTypeId = default, CancellationToken cancellation = default)
    {
        var columns = new Dictionary<string, ColumnDefinition>();
        var properties = new string[] { "id", "name", "text", "dateTime", "lookup", "hidden" };
        var response = string.IsNullOrEmpty(contentTypeId) ?
            await Graph.Sites[siteId].Columns.GetAsync(r => r.QueryParameters.Select = properties, cancellation) :
            await Graph.Sites[siteId].ContentTypes[contentTypeId].Columns.GetAsync(r => r.QueryParameters.Select = properties, cancellation);

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

            await pager.IterateAsync(cancellation);
        }

        return columns;
    }

    private async Task<string> EnsureListContentTypeAsync(string siteId, string listId, string siteContentTypeId, int tries, CancellationToken cancellation)
    {
        var existing = await Graph.Sites[siteId].Lists[listId].ContentTypes.GetAsync(cancellationToken: cancellation);
        var match = existing?.Value?.FirstOrDefault(ct => ct.Name == ContentTypeName);

        if (match?.Id != null) return match.Id;

        ContentType? copy = null;

        try
        {
            copy = await Graph.Sites[siteId].Lists[listId].ContentTypes.AddCopy.PostAsync(
            new Microsoft.Graph.Sites.Item.Lists.Item.ContentTypes.AddCopy.AddCopyPostRequestBody
            {
                ContentType = $"https://graph.microsoft.com/v1.0/sites/{siteId}/contentTypes/{siteContentTypeId}"
            },
            null,
            cancellation) ?? throw new InvalidOperationException($"Failed to add content type '{ContentTypeName}' to list '{listId}'.");
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == (int)System.Net.HttpStatusCode.Conflict && tries < 10)
        {
            var delay = TimeSpan.FromSeconds(2 * tries) + TimeSpan.FromMilliseconds(Random.Shared.Next());
            logger.LogDebug("Conflict encountered while creating content type id {siteContentTypeId} '{ListId}'. Retrying ({Try}/10) in {Delay}.", siteContentTypeId, listId, tries, delay);
            await Task.Delay(delay, cancellation);
            return await EnsureListContentTypeAsync(siteId, listId, siteContentTypeId, tries + 1, cancellation);
        }

        logger.LogDebug("Added content type '{ContentTypeName}' to list '{ListId}'.", ContentTypeName, listId);

        return copy.Id ?? throw new InvalidOperationException($"Content type '{ContentTypeName}' was added to list '{listId}' without an id.");
    }

    private async Task<string> GetListIdAsync(string driveId, int tries, CancellationToken cancellation)
    {
        var list = await Graph.Drives[driveId].List.GetAsync(cancellationToken: cancellation)
            ?? throw new InvalidOperationException($"Failed to resolve the list for drive '{driveId}'.");

        if (list.ListProp?.ContentTypesEnabled == false)
        {
            try
            {
                await Graph.Drives[driveId].List.PatchAsync(
                    new List
                    {
                        AdditionalData = new Dictionary<string, object>
                        {
                            { "@odata.etag", list.AdditionalData["@odata.etag"] }
                        },
                        ListProp = new ListInfo { ContentTypesEnabled = true }
                    },
                    null,
                    cancellation);
            }
            catch (ODataError odataError) when(odataError.ResponseStatusCode == (int)System.Net.HttpStatusCode.Conflict && tries < 10)
            {
                var delay = TimeSpan.FromSeconds(2 * tries) + TimeSpan.FromMilliseconds(Random.Shared.Next());
                logger.LogDebug("Conflict encountered while enabling content types for list '{ListId}'. Retrying ({Try}/10) in {Delay}.", list.Id, tries, delay);
                await Task.Delay(delay, cancellation);
                return await GetListIdAsync(driveId, tries + 1, cancellation);
            }
        }

        return list.Id ?? throw new InvalidOperationException($"The list for drive '{driveId}' does not have an id.");
    }

    private async Task<string> GetListItemIdAsync(string driveId, string path)
    {
        ListItem? listItem = null;

        try
        {
            listItem = await Graph.Drives[driveId].Root.ItemWithPath(path).ListItem.GetAsync()
                ?? throw new InvalidOperationException($"No list item found for drive item '{path}' in drive '{driveId}'.");
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError, "Failed to retrieve list item for drive '{DriveId}' at path '{Path}'.", driveId, path);

            throw;
        }

        return listItem.Id ?? throw new InvalidOperationException($"The list item for drive item {path} does not have an id.");
    }

    private async Task<ContentType?> FindContentTypeAsync(string siteId, string name)
    {
        var contentTypes = await Graph.Sites[siteId].ContentTypes.GetAsync(r =>
        {
            r.QueryParameters.Filter = $"name eq '{name}'";
        });

        return contentTypes?.Value?.FirstOrDefault(ct => ct.Name == name);
    }

    private string GetBaseContentTypeId() => schema.Type switch
    {
        SharePointSchemaType.Folder => "0x0120",
        _ => "0x0101"
    };
}

public class SiteIdProvider(IntegrationGraphClient graphClient)
{
    private readonly ConcurrentDictionary<string, string> _siteIds = [];

    public async Task<string> ResolveSiteIdAsync(string driveId)
    {
        if (_siteIds.TryGetValue(driveId, out var cached)) return cached;

        var root = await graphClient.Drives[driveId].Root.GetAsync(config => config.QueryParameters.Select = ["sharepointIds"])
            ?? throw new InvalidOperationException($"Failed to resolve the root folder for drive '{driveId}'.");

        var siteId = root.SharepointIds?.SiteId
            ?? throw new InvalidOperationException($"Failed to resolve the site id for drive '{driveId}'.");

        _siteIds[driveId] = siteId;

        return siteId;
    }
}

/// <summary>
/// The identifiers required to read and write metadata for a content type in a document library.
/// </summary>
public record ContentTypeKey(string ListId, string ContentTypeId);
