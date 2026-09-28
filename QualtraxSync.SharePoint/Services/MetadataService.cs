using Microsoft.Extensions.Logging;
using QualtraxSync.SharePoint.Client;
using QualtraxSync.SharePoint.Services.Helpers;
using QualtraxSync.Contracts.Services;
using System.Collections.Concurrent;
using System.Reflection;

namespace QualtraxSync.SharePoint.Services;

public class MetadataService(
    IntegrationGraphClient graphClient,
    IListService listService,
    IEnumerable<ISchemaConfiguration> configurations,
    ILoggerFactory loggerFactory) : IMetadataService
{
    private readonly ConcurrentDictionary<Type, object> _providers = new();
    private readonly SiteIdProvider _siteIdProvider = new(graphClient);

    public async Task UpdateAsync<T>(string driveId, string path, T metadata, CancellationToken cancellationToken) where T : class
    {
        var provider = await GetAsync<T>(driveId);
        await provider.UpdateAsync(driveId, path, metadata, cancellationToken);
    }

    public async Task<T> GetAsync<T>(string driveId, string path, CancellationToken cancellationToken) where T : class 
    {
        var provider = await GetAsync<T>(driveId);
        return await provider.GetAsync(driveId, path, cancellationToken);
    }

    public async Task<Dictionary<string, T>> GetAllAsync<T>(string driveId, CancellationToken cancellationToken) where T : class
    {
        var provider = await GetAsync<T>(driveId);
        return await provider.GetAllAsync(driveId, cancellationToken);
    }

    public Task ProvisionAsync(string driveId, Type type, CancellationToken cancellationToken)
    {
        var method = typeof(MetadataService).GetMethod(nameof(ProvisionAsync), BindingFlags.NonPublic | BindingFlags.Instance)?.MakeGenericMethod(type)
            ?? throw new InvalidOperationException($"Failed to retrieve {nameof(ProvisionAsync)} method for {type.Name} type.");

        return (Task?)method.Invoke(this, [..driveId, cancellationToken])
            ?? throw new InvalidOperationException($"Failed to invoke {nameof(ProvisionAsync)} for {type.Name} type.");
    }

    private async Task ProvisionAsync<T>(string driveId, CancellationToken cancellationToken) where T : class
    {
        var provider = await GetAsync<T> (driveId);
        await provider.ProvisionAsync(driveId, cancellationToken);
    }

    private async Task<MetadataProvider<T>> GetAsync<T>(string driveId) where T : class
    {
        var siteId = await _siteIdProvider.ResolveSiteIdAsync(driveId);
        return (MetadataProvider<T>) _providers.GetOrAdd(typeof(T), _ => Create<T>(siteId));
    }

    private MetadataProvider<T> Create<T>(string siteId) where T : class
    {
        var logger = loggerFactory.CreateLogger<MetadataProvider<T>>();
        var schema = SiteSchema<T>.Create(configurations, siteId);

        return new MetadataProvider<T>(graphClient, listService, schema, _siteIdProvider, logger);
    }
}