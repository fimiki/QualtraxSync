using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using QualtraxSync.Contracts.Services;

namespace QualtraxSync.SharePoint.Test.Fakes;

/// <summary>
/// A lookup-list backed model used to exercise the lookup branches of field value serialization/deserialization.
/// </summary>
public class LookupItem
{
    public string? Id { get; set; }

    public string? Name { get; set; }
}

/// <summary>
/// A model exercising every branch of <see cref="Services.Helpers.FieldValueSerialization"/> and
/// <see cref="Services.Helpers.FieldValueDeserialization"/>: plain value types, strings, dates, uris (with and
/// without a description property), single/array lookups, and a read-only property that should be skipped entirely.
/// </summary>
public class FieldTestModel
{
    public string? Text { get; set; }

    public int? Number { get; set; }

    public bool? Flag { get; set; }

    public DateTime? When { get; set; }

    public DateOnly? Day { get; set; }

    public Uri? Link { get; set; }

    [Display(Name = nameof(LinkDescription))]
    public Uri? DescribedLink { get; set; }

    public string? LinkDescription { get; set; }

    public LookupItem? SingleLookup { get; set; }

    public LookupItem[]? MultiLookup { get; set; }

    [ReadOnly(true)]
    public string? Computed { get; set; }
}

/// <summary>
/// An in-memory stand-in for <see cref="IListService"/> that treats <see cref="LookupItem"/> as
/// the only registered lookup list type.
/// </summary>
internal sealed class FakeListService : IListService
{
    private readonly Dictionary<string, LookupItem> _items = [];

    public void Seed(string id, LookupItem item) => _items[id] = item;

    public ListKey? GetKey(string siteId, Type listType) =>
        listType == typeof(LookupItem) ? new ListKey("lookup-list-id", "LookupField") : null;

    public Task ProvisionAsync(string siteId, Type listType) => Task.CompletedTask;

    public Task<string> GetIdAsync<T>(string siteId, T value) where T : class =>
        Task.FromResult(((LookupItem)(object)value!).Id!);

    public T GetValue<T>(string siteId, string lookupId) where T : class =>
        (T)(object)_items[lookupId];

    public object GetValue(string siteId, string lookupId, Type listType) => _items[lookupId];

    public Task UpdateAsync<T>(string siteId, bool removeIfMissing, params T[] values) where T : class =>
        Task.CompletedTask;
}
