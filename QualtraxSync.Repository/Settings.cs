using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Repositories;
using QualtraxSync.Persistence.Repositories;
using QualtraxSync.Persistence.Services;
using QualtraxSync.Persistence.Services.Materializers;

namespace QualtraxSync.Persistence;

public static class Settings
{
    public static readonly Configuration Options = new();

    public static IServiceCollection AddPersistence(this IServiceCollection services, Action<Configuration> options)
    {
        options.Invoke(Options);

        services.AddOptions<Options>().Bind(Options.PersistenceConfig);
        services.AddLocalServices();
        services.AddDomainImplementations();
        services.AddDomainEventHandlers();

        return services;
    }

    private static IServiceCollection AddLocalServices(this IServiceCollection services)
    {
        services.AddSingleton(typeof(EntityStore<,>));
        services.AddSingleton<FolderMaterializer>();
        services.AddSingleton<DocumentMaterializer>();
        services.AddSingleton<RevisionMaterializer>();
        services.AddSingleton<FolderRoots>();
        services.AddSingleton<Initializer>();
        services.AddSingleton<PathService>();

        return services;
    }

    private static IServiceCollection AddDomainImplementations(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();

        return services;
    }

    private static IServiceCollection AddDomainEventHandlers(this IServiceCollection services)
    {
        services.Scan(scan => scan.FromAssemblyOf<Options>()
            .AddClasses(classes => classes.AssignableTo<INotificationHandler>())
            .AsSelfWithInterfaces()
            .WithTransientLifetime()
        );
        services.AddTransient<NotificationDispatcher>();

        return services;
    }

    public class Configuration
    {
        public IConfigurationSection PersistenceConfig { get; set; } = null!;

        public Options Persistence => PersistenceConfig.Get<Options>() ?? throw new Exception("No Persistence app settings found");
    }
}
