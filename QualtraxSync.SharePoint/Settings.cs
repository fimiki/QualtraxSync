using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QualtraxSync.Contracts.Services;
using QualtraxSync.SharePoint.Client;
using QualtraxSync.SharePoint.Client.Middleware;
using QualtraxSync.SharePoint.Services;
using System.Reflection;

namespace QualtraxSync.SharePoint;

public static class Settings
{
    private static readonly Configuration Options = new();

    public static IServiceCollection AddSharePoint(this IServiceCollection services, Action<Configuration> options)
    {
        options.Invoke(Options);

        services.AddOptions<Options>().Bind(Options.AzureConfig);
        services.AddGraphClient();

        services.AddSiteSchemaConfigurations(Options.SchemaConfigurations);
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IListService, ListService>();
        services.AddSingleton<IMetadataService, MetadataService>();

        return services;
    }

    private static IServiceCollection AddGraphClient(this IServiceCollection services)
    {
        services.AddSingleton<CompleteJobWithDelayHandler>();
        services.AddSingleton<IntegrationGraphClient>();

        return services;
    }

    private static IServiceCollection AddSiteSchemaConfigurations(this IServiceCollection services, IEnumerable<Assembly> assemblies)
    {
        services.Scan(scan => scan.FromAssemblies(assemblies)
            .AddClasses(classes => classes.AssignableTo(typeof(ISchemaConfiguration<>)))
            .AsSelfWithInterfaces()
            .WithSingletonLifetime()
        );

        return services;
    }

    public class Configuration
    {
        public IConfigurationSection AzureConfig { get; set; } = null!;

        public IEnumerable<Assembly> SchemaConfigurations { get; set; } = [];

        internal Options Azure => AzureConfig.Get<Options>() ?? throw new Exception("No Azure app settings found");
    }
}
