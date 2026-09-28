using Microsoft.Extensions.Logging.Abstractions;
using QualtraxSync.SharePoint.Client;
using QualtraxSync.SharePoint.Client.Middleware;

namespace QualtraxSync.SharePoint.Test;

/// <summary>
/// Shared helpers for constructing SharePoint service dependencies without making any real network calls.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Creates an <see cref="IntegrationGraphClient"/> with fake credentials. Safe to construct in tests
    /// (credential validation is lazy and only occurs when a request is actually sent), but any method that
    /// issues a real Graph request must not be invoked against an instance created this way.
    /// </summary>
    public static IntegrationGraphClient CreateGraphClient()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new Options
        {
            Auth = new AuthOptions
            {
                TenantId = "00000000-0000-0000-0000-000000000000",
                ClientId = "00000000-0000-0000-0000-000000000000",
                ClientSecret = "fake-secret"
            }
        });

        var jobHandler = new CompleteJobWithDelayHandler(NullLogger<CompleteJobWithDelayHandler>.Instance);

        return new IntegrationGraphClient(options, jobHandler);
    }
}
