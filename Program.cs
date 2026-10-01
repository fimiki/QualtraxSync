using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QualtraxSync.Persistence;
using QualtraxSync.Qualtrax;
using QualtraxSync.Services;
using QualtraxSync.SharePoint;
using Serilog;
using Serilog.Events;

namespace QualtraxSync;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        // anchor the executable's directory so relative paths resolve consistently for both console and service hosting.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        var builder = Host.CreateApplicationBuilder();

        // command line takes precedence over environment variables, which take precedence over appsettings.json
        builder.Configuration.AddCommandLine(args, SwitchMappings);

        Log.Logger = CreateLogger(builder.Configuration);

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);

        try
        {
            builder.Services.AddWindowsService(options => options.ServiceName = "QualtraxSync");
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddQualtrax(qualtrax => qualtrax.QualtraxConfig = builder.Configuration.GetSection(Qualtrax.Options.Section));
            builder.Services.AddSharePoint(sharePoint =>
            {
                sharePoint.AzureConfig = builder.Configuration.GetSection(SharePoint.Options.Section);
                sharePoint.SchemaConfigurations = [typeof(Persistence.Settings).Assembly];
            });
            builder.Services.AddPersistence(persistence => persistence.PersistenceConfig = builder.Configuration.GetSection(Persistence.Options.Section));
            builder.Services.AddSyncService(sync =>
            {
                sync.SyncConfig = builder.Configuration.GetSection(Services.Options.Section);
                sync.IncludeArchived = Persistence.Settings.Options.Persistence.Lifecycle.IncludeArchived;
                sync.IncludeRetired = Persistence.Settings.Options.Persistence.Lifecycle.IncludeRetired;
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

    private static Serilog.Core.Logger CreateLogger(IConfiguration configuration)
    {
        var loggerConfiguration = new LoggerConfiguration().ReadFrom.Configuration(configuration);

        // Application Insights is an optional sink: it's only enabled when a connection string is configured
        // (e.g. via ApplicationInsights:ConnectionString, environment variables or user secrets).
        var appInsightsConnectionString = configuration["ApplicationInsights:ConnectionString"];

        if (string.IsNullOrWhiteSpace(appInsightsConnectionString) == false)
        {
            var telemetryConfiguration = TelemetryConfiguration.CreateDefault();
            telemetryConfiguration.ConnectionString = appInsightsConnectionString;
            loggerConfiguration.WriteTo.ApplicationInsights(telemetryConfiguration, TelemetryConverter.Traces, LogEventLevel.Verbose);
        }
        else if (configuration.GetSection("Serilog:WriteTo").GetChildren().Any() == false)
        {
            loggerConfiguration.WriteTo.Console();
        }

        if (configuration.GetSection("Serilog:MinimumLevel").GetChildren().Any() == false)
        {
            loggerConfiguration.MinimumLevel.Error();
            loggerConfiguration.MinimumLevel.Override(nameof(QualtraxSync), LogEventLevel.Information);
        }

        return loggerConfiguration.CreateLogger();
    }
}
