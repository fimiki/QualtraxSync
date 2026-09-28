using Microsoft.Extensions.DependencyInjection;
using Polly;
using System.Net;

namespace QualtraxSync.Qualtrax.Client;

internal static class HttpClientExtensions
{
    internal static IServiceCollection AddApiClient(
        this IServiceCollection services,
        Options options)
    {
        services
            .AddHttpClient<IApiClient, ApiClient>(ApiClient.Name, client =>
            {
                client.BaseAddress = new Uri(options.Url);
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.Token);
                client.DefaultRequestHeaders.UserAgent
                    .ParseAdd(options.UserAgent);
                client.DefaultRequestHeaders.Accept
                    .ParseAdd("application/vnd.qualtrax.v1+json");
                client.DefaultRequestHeaders.AcceptEncoding
                    .ParseAdd("gzip, deflate, br");
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(10))
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(20);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(20); 
                options.Retry.MaxRetryAttempts = 10;
                options.Retry.Delay = TimeSpan.FromSeconds(2);
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
            });

        return services;
    }
}