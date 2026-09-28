using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QualtraxSync.Persistence;
using QualtraxSync.Qualtrax;
using QualtraxSync.Services;
using QualtraxSync.SharePoint;
using Serilog;

namespace QualtraxSync;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // command line takes precedence over environment variables, which take precedence over appsettings.json
        builder.Configuration.AddCommandLine(args, SwitchMappings);

        // sinks, levels and enrichers are all read from configuration, so they can come from any of the sources above
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);

        try
        {
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddQualtrax(qualtrax => qualtrax.QualtraxConfig = builder.Configuration.GetSection(Qualtrax.Options.Section));
            builder.Services.AddSharePoint(sharePoint =>
            {
                sharePoint.AzureConfig = builder.Configuration.GetSection(SharePoint.Options.Section);
                sharePoint.SchemaConfigurations = [typeof(Persistence.Settings).Assembly];
            });
            builder.Services.AddPersistence(persistence => persistence.RepositoryConfig = builder.Configuration.GetSection(Persistence.Options.Section));
            builder.Services.AddSyncService(sync =>
            {
                sync.SyncConfig = builder.Configuration.GetSection(Services.Options.Section);
                sync.SyncAllRevisions = builder.Configuration.GetValue<bool>("SharePoint:Lifecycle:IncludeArchived");

            });
            builder.Services.AddHostedService<Worker>();

            await builder.Build().RunAsync();

            return 0;
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "QualtraxSync terminated unexpectedly.");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Command line switches for every Options class in the solution, mapped onto their configuration keys.
    /// </summary>
    private static Dictionary<string, string> SwitchMappings =>
        new Dictionary<string, string>[]
        {
            Qualtrax.Options.Map,
            SharePoint.Options.Map,
            Persistence.Options.Map,
            Services.Options.Map
        }
        .SelectMany(map => map)
        .ToDictionary(entry => entry.Key, entry => entry.Value);

}
