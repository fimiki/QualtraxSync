using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QualtraxSync.SharePoint.Services;

namespace QualtraxSync.SharePoint.Test;

/// <summary>
/// Covers the parts of <see cref="ListService"/> that don't require issuing a real Microsoft Graph request:
/// cache lookups against an empty/unpopulated state. Methods that reach out to Graph (e.g. <c>GetIdAsync</c>,
/// <c>UpdateAsync</c>, <c>ProvisionAsync</c>) are not covered here since they cannot be exercised without a
/// mockable abstraction over <see cref="Client.IntegrationGraphClient"/>.
/// </summary>
public class ListServiceTests
{
    private class TestLookup
    {
        public string? Id { get; set; }
    }

    [Fact]
    public void GetKeyAsync_ReturnsNull_ForUnregisteredType()
    {
        var service = CreateService();

        var key = service.GetKey("site-1", typeof(TestLookup));

        Assert.Null(key);
    }

    [Fact]
    public void GetValue_NonGeneric_Throws_WhenLookupNotCached()
    {
        var service = CreateService();

        // The non-generic overload dispatches to the generic GetValue<T> via reflection, so the
        // underlying exception is wrapped in a TargetInvocationException.
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => service.GetValue("site-1", "item-1", typeof(TestLookup)));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    private static ListService CreateService()
    {
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new ListService(
            TestHelpers.CreateGraphClient(),
            scopeFactory,
            configurations: [],
            NullLoggerFactory.Instance);
    }
}
