using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QualtraxSync.SharePoint.Services;

namespace QualtraxSync.SharePoint.Test;

/// <summary>
/// <see cref="MetadataService"/> resolves the SharePoint site id for every operation by calling Microsoft Graph
/// (see <c>SiteIdProvider.ResolveSiteIdAsync</c>), so its public methods cannot be unit tested without a
/// mockable abstraction over <see cref="Client.IntegrationGraphClient"/>. Only
/// construction (which performs no I/O) is covered here.
/// </summary>
public class MetadataServiceTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var listService = new ListService(
            TestHelpers.CreateGraphClient(),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            configurations: [],
            NullLoggerFactory.Instance);

        var exception = Record.Exception(() => new MetadataService(
            TestHelpers.CreateGraphClient(),
            listService,
            configurations: [],
            NullLoggerFactory.Instance));

        Assert.Null(exception);
    }
}
