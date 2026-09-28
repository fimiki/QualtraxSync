using QualtraxSync.SharePoint.Services.Helpers;
using QualtraxSync.SharePoint.Test.Fakes;
using System.Text.Json;

namespace QualtraxSync.SharePoint.Test;

/// <summary>
/// Covers <see cref="FieldValueSerialization"/> and <see cref="FieldValueDeserialization"/>, which convert between
/// strongly-typed model properties and the raw field dictionaries used by SharePoint list items.
/// </summary>
public class FieldValueSerializationTests
{
    private const string SiteId = "site-1";

    [Fact]
    public async Task GetFieldValuesAsync_SerializesPlainValueTypes()
    {
        var model = new FieldTestModel
        {
            Text = "hello",
            Number = 42,
            Flag = true
        };

        var values = await model.GetFieldValuesAsync(SiteId, new FakeListService());

        Assert.Equal("hello", values[nameof(FieldTestModel.Text)]);
        Assert.Equal(42, values[nameof(FieldTestModel.Number)]);
        Assert.Equal(true, values[nameof(FieldTestModel.Flag)]);
    }

    [Fact]
    public async Task GetFieldValuesAsync_SkipsReadOnlyProperties()
    {
        var model = new FieldTestModel { Computed = "should not be serialized" };

        var values = await model.GetFieldValuesAsync(SiteId, new FakeListService());

        Assert.False(values.ContainsKey(nameof(FieldTestModel.Computed)));
    }

    [Fact]
    public async Task GetFieldValuesAsync_NullValue_SerializesAsNullEntry()
    {
        var model = new FieldTestModel { Text = null };

        var values = await model.GetFieldValuesAsync(SiteId, new FakeListService());

        Assert.True(values.ContainsKey(nameof(FieldTestModel.Text)));
        Assert.Null(values[nameof(FieldTestModel.Text)]);
    }

    [Fact]
    public async Task GetFieldValuesAsync_SingleLookup_SerializesAsLookupIdField()
    {
        var listService = new FakeListService();
        listService.Seed("7", new LookupItem { Id = "7", Name = "Widget" });

        var model = new FieldTestModel { SingleLookup = new LookupItem { Id = "7", Name = "Widget" } };

        var values = await model.GetFieldValuesAsync(SiteId, listService);

        Assert.Equal("7", values[nameof(FieldTestModel.SingleLookup) + "LookupId"]);
        Assert.False(values.ContainsKey(nameof(FieldTestModel.SingleLookup)));
    }

    [Fact]
    public async Task GetFieldValuesAsync_NullLookup_SerializesLookupIdFieldAsNull()
    {
        var model = new FieldTestModel { SingleLookup = null };

        var values = await model.GetFieldValuesAsync(SiteId, new FakeListService());

        Assert.True(values.ContainsKey(nameof(FieldTestModel.SingleLookup) + "LookupId"));
        Assert.Null(values[nameof(FieldTestModel.SingleLookup) + "LookupId"]);
    }

    [Fact]
    public async Task GetFieldValuesAsync_MultiLookup_SerializesAsSemicolonDelimitedIds()
    {
        var listService = new FakeListService();
        listService.Seed("1", new LookupItem { Id = "1", Name = "A" });
        listService.Seed("2", new LookupItem { Id = "2", Name = "B" });

        var model = new FieldTestModel
        {
            MultiLookup =
            [
                new LookupItem { Id = "1", Name = "A" },
                new LookupItem { Id = "2", Name = "B" }
            ]
        };

        var values = await model.GetFieldValuesAsync(SiteId, listService);

        Assert.Equal("1;#2", values[nameof(FieldTestModel.MultiLookup) + "LookupId"]);
    }

    [Fact]
    public void GetMetadata_DeserializesPlainValueTypesAndStrings()
    {
        var fields = new Dictionary<string, object>
        {
            [nameof(FieldTestModel.Text)] = "hello",
            [nameof(FieldTestModel.Number)] = 42,
            [nameof(FieldTestModel.Flag)] = true
        };

        var model = fields.GetMetadata<FieldTestModel>(new FakeListService(), SiteId);

        Assert.Equal("hello", model.Text);
        Assert.Equal(42, model.Number);
        Assert.True(model.Flag);
    }

    [Fact]
    public void GetMetadata_DeserializesDateOnlyAndDateTime()
    {
        var json = """{ "When": "2024-06-01T13:30:00Z", "Day": "2024-06-01" }""";
        var element = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        var fields = new Dictionary<string, object>
        {
            [nameof(FieldTestModel.When)] = element[nameof(FieldTestModel.When)],
            [nameof(FieldTestModel.Day)] = element[nameof(FieldTestModel.Day)]
        };

        var model = fields.GetMetadata<FieldTestModel>(new FakeListService(), SiteId);

        Assert.Equal(new DateOnly(2024, 6, 1), model.Day);
        Assert.Equal(new DateTime(2024, 6, 1, 13, 30, 0, DateTimeKind.Utc), model.When!.Value.ToUniversalTime());
    }

    [Fact]
    public void GetMetadata_DeserializesUri_FromDictionaryFormat()
    {
        var fields = new Dictionary<string, object>
        {
            [nameof(FieldTestModel.Link)] = new Dictionary<string, object> { ["Url"] = "https://example.com/doc", ["Description"] = "View" }
        };

        var model = fields.GetMetadata<FieldTestModel>(new FakeListService(), SiteId);

        Assert.Equal(new Uri("https://example.com/doc"), model.Link);
    }

    [Fact]
    public void GetMetadata_DeserializesUri_FromJsonElementObject()
    {
        var json = """{ "Url": "https://example.com/doc", "Description": "View" }""";
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        var fields = new Dictionary<string, object>
        {
            [nameof(FieldTestModel.Link)] = element
        };

        var model = fields.GetMetadata<FieldTestModel>(new FakeListService(), SiteId);

        Assert.Equal(new Uri("https://example.com/doc"), model.Link);
    }

    [Fact]
    public void GetMetadata_MissingField_LeavesPropertyAsDefault()
    {
        var fields = new Dictionary<string, object>();

        var model = fields.GetMetadata<FieldTestModel>(new FakeListService(), SiteId);

        Assert.Null(model.Text);
        Assert.Null(model.Number);
    }

    [Fact]
    public void GetMetadata_SingleLookup_DeserializesFromLookupIdField()
    {
        var listService = new FakeListService();
        listService.Seed("7", new LookupItem { Id = "7", Name = "Widget" });

        var fields = new Dictionary<string, object>
        {
            [nameof(FieldTestModel.SingleLookup) + "LookupId"] = "7"
        };

        var model = fields.GetMetadata<FieldTestModel>(listService, SiteId);

        Assert.NotNull(model.SingleLookup);
        Assert.Equal("Widget", model.SingleLookup!.Name);
    }

    [Fact]
    public void GetMetadata_MultiLookup_DeserializesFromSemicolonDelimitedIds()
    {
        var listService = new FakeListService();
        listService.Seed("1", new LookupItem { Id = "1", Name = "A" });
        listService.Seed("2", new LookupItem { Id = "2", Name = "B" });

        var fields = new Dictionary<string, object>
        {
            [nameof(FieldTestModel.MultiLookup) + "LookupId"] = "1;#2"
        };

        var model = fields.GetMetadata<FieldTestModel>(listService, SiteId);

        Assert.NotNull(model.MultiLookup);
        Assert.Equal(2, model.MultiLookup!.Length);
        Assert.Equal("A", model.MultiLookup[0].Name);
        Assert.Equal("B", model.MultiLookup[1].Name);
    }

    [Fact]
    public void GetMetadata_MissingLookupIdField_LeavesLookupPropertyNull()
    {
        var fields = new Dictionary<string, object>();

        var model = fields.GetMetadata<FieldTestModel>(new FakeListService(), SiteId);

        Assert.Null(model.SingleLookup);
        Assert.Null(model.MultiLookup);
    }

    [Fact]
    public async Task RoundTrip_SerializeThenDeserialize_PreservesPlainValues()
    {
        var listService = new FakeListService();
        listService.Seed("7", new LookupItem { Id = "7", Name = "Widget" });

        var original = new FieldTestModel
        {
            Text = "hello",
            Number = 42,
            Flag = true,
            SingleLookup = new LookupItem { Id = "7", Name = "Widget" }
        };

        var values = await original.GetFieldValuesAsync(SiteId, listService);
        var fields = values.ToDictionary(kv => kv.Key, kv => kv.Value!);

        var roundTripped = fields.GetMetadata<FieldTestModel>(listService, SiteId);

        Assert.Equal(original.Text, roundTripped.Text);
        Assert.Equal(original.Number, roundTripped.Number);
        Assert.Equal(original.Flag, roundTripped.Flag);
        Assert.Equal(original.SingleLookup.Name, roundTripped.SingleLookup!.Name);
    }
}
