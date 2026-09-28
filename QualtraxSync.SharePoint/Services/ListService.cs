using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QualtraxSync.SharePoint.Client;
using QualtraxSync.SharePoint.Services.Helpers;
using QualtraxSync.Contracts.Services;
using System.Collections.Concurrent;
using System.Reflection;

namespace QualtraxSync.SharePoint.Services;


public class ListService(
    IntegrationGraphClient graphClient,
    IServiceScopeFactory serviceScopeFactory,
    IEnumerable<ISchemaConfiguration> configurations,
    ILoggerFactory loggerFactory) : IListService
{
    private readonly ConcurrentDictionary<Type, ISiteList> _lists = new();

    public Task ProvisionAsync(string siteId, Type listType)
    {
        var task = (Task?)EnsureListAsyncMethod(listType).Invoke(this, [siteId]);

        return task ?? throw new InvalidOperationException($"Failed to invoke {nameof(ProvisionAsync)} for {listType.Name} type.");
    }

    public Task<string> GetIdAsync<T>(string siteId, T value) where T : class =>
        Get<T>(siteId).Instance.GetLookupIdAsync(siteId, value);

    public T GetValue<T>(string siteId, string itemId) where T : class =>
        Get<T>(siteId).Instance.GetLookupValue(siteId, itemId);

    public object GetValue(string siteId, string itemId, Type listType)
    {
        var method = typeof(ListService)
            .GetMethod(nameof(GetValue), [typeof(string), typeof(string)])
            ?? throw new InvalidOperationException($"Failed to retrieve {nameof(GetValue)} method.");

        return method.MakeGenericMethod(listType).Invoke(this, [siteId, itemId])
            ?? throw new InvalidOperationException($"Failed to get lookup value for {listType.Name}.");
    }

    public ListKey? GetKey(string siteId, Type listType) 
    {
        if (_lists.TryGetValue(listType, out var list))
        {
            return list.GetKey(siteId);
        }
         
        return null;
    }

    public Task UpdateAsync<T>(string siteId, bool removeIfMissing, params T[] values) where T : class =>
        Get<T>(siteId).Instance.UpdateAsync(siteId, removeIfMissing, values);

    private async Task EnsureListAsync<T>(string siteId) where T : class
    {
        if (GetKey(siteId, typeof(T)) != null) return;

        await Get<T>(siteId).EnsureLookupListAsync(siteId);
    }

    private static MethodInfo EnsureListAsyncMethod(Type type) =>
        typeof(ListService).GetMethod(nameof(EnsureListAsync), BindingFlags.NonPublic | BindingFlags.Instance)?.MakeGenericMethod(type)
        ?? throw new InvalidOperationException($"Failed to retrieve {nameof(EnsureListAsync)} method for {type.Name} type.");

    private AppListProvider<T> Get<T>(string siteId) where T : class => (AppListProvider<T>)_lists.GetOrAdd(typeof(T), _ => Create<T>(siteId));

    private AppListProvider<T> Create<T>(string siteId) where T : class
    {
        var logger = loggerFactory.CreateLogger<ListProvider<T>>();
        logger.LogDebug("Creating list provider for type {TypeName}", typeof(T).Name);

        var schema = SiteSchema<T>.Create(configurations, siteId);
        var instance = new ListProvider<T>(graphClient, this, serviceScopeFactory, schema, logger);

        return new AppListProvider<T>(instance);
    }

    private record AppListProvider<T>(ListProvider<T> Instance) : ISiteList where T : class
    {
        private readonly ConcurrentDictionary<string, ListKey> _keys = [];
        private readonly ConcurrentDictionary<string, Task> _ensuring = [];

        public ListKey? GetKey(string siteId) => _keys.TryGetValue(siteId, out var key) ? key : null;

        public Task EnsureLookupListAsync(string siteId) => _ensuring.GetOrAdd(siteId, _ => AddKeyForLookupAsync(siteId));

        private async Task AddKeyForLookupAsync(string siteId)
        {
            var key = await Instance.EnsureLookupListAsync(siteId);
            _keys[siteId] = key;
        }
    }

    private interface ISiteList
    {
        ListKey? GetKey(string siteId);
    }
}