using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Qualtrax.Client;
using QualtraxSync.Qualtrax.Service;
using QualtraxSync.Qualtrax.Services;

namespace QualtraxSync.Qualtrax;

public static class Settings
{
    private static readonly Configuration Options = new();

    public static IServiceCollection AddQualtrax(this IServiceCollection services, Action<Configuration> options)
    {
        options.Invoke(Options);

        services.AddOptions<Options>().Bind(Options.QualtraxConfig);
        services.AddApiClient(Options.Qualtrax);
        services.AddScoped<IQualtraxItemService, QualtraxItemService>();
        services.AddScoped<IQualtraxApiService, QualtraxApiService>();

        return services;
    }

    public class Configuration
    {
        public IConfigurationSection QualtraxConfig { get; set; } = null!;

        internal Options Qualtrax => QualtraxConfig.Get<Options>() ?? throw new Exception("No Qualtrax settings found");
    }
}
