using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using QualtraxSync.Services.Services;

namespace QualtraxSync.Services;

public static class Settings
{
    private static readonly Configuration Options = new();

    public static IServiceCollection AddSyncService(this IServiceCollection services, Action<Configuration> options)
    {
        options.Invoke(Options);

        services.AddOptions<Options>().Bind(Options.SyncConfig).Configure(options => options.SyncAllRevisions = Options.SyncAllRevisions);
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<ISyncService, SyncService>();

        return services;
    }

    public class Configuration
    {
        public IConfigurationSection SyncConfig { get; set; } = null!;

        internal Options Sync => SyncConfig.Get<Options>() ?? throw new Exception("No Sync settings found");

        public bool SyncAllRevisions { get; set; } = false;
    }
}
