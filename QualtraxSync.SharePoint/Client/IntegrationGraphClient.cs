using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions.Serialization;
using QualtraxSync.SharePoint.Client.Middleware;
using System.Security.Cryptography.X509Certificates;

namespace QualtraxSync.SharePoint.Client;

public class IntegrationGraphClient(
    IOptions<Options> options,
    CompleteJobWithDelayHandler jobHandler) : GraphServiceClient(GetHttpClient(jobHandler), GetCredentials(options))
{
    public Dictionary<string, ParsableFactory<IParsable>> GetDefaultErrorMapper() => new() { { "XXX", ODataError.CreateFromDiscriminatorValue } };
    private static ClientCertificateCredential GetCertificateCredentials(IOptions<Options> options)
    {
        return new ClientCertificateCredential(
            options.Value.Auth.TenantId,
            options.Value.Auth.ClientId,
            GetCertificate(string.Empty));
    }

    private static ClientSecretCredential GetCredentials(IOptions<Options> options)
    {
        return new ClientSecretCredential(
            options.Value.Auth.TenantId,
            options.Value.Auth.ClientId,
            options.Value.Auth.ClientSecret);
    }

    private static X509Certificate2? GetCertificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var certificates = store.Certificates;

        return certificates.Count == 0 ? null : certificates.FirstOrDefault(c => c.Thumbprint == thumbprint);
    }

    private static HttpClient GetHttpClient(
        CompleteJobWithDelayHandler jobHandler)
    {
        var handlers = GraphClientFactory.CreateDefaultHandlers();
        jobHandler.SetDelay(TimeSpan.FromSeconds(2).TotalMilliseconds);
        handlers.Add(jobHandler);
        return GraphClientFactory.Create(handlers);
    }
}
