using QualtraxSync.Contracts.Services;
using QualtraxSync.SharePoint.Configuration.Helpers;
using System.Linq.Expressions;
using System.Reflection;

namespace QualtraxSync.SharePoint.Services.Helpers;

/// <summary>
/// Holds the resolved configuration for a SharePoint content type or list schema.
/// Constructed by <see cref="SiteSchemaBuilder{T}"/> from <see cref="ISchemaConfiguration{T}"/> implementations.
/// </summary>
public sealed class SiteSchema<T> where T : class
{
    internal SiteSchema(string name, string description, SharePointSchemaType type, PropertyNameMapBuilder<T> nameMap, PropertyInfo? lookupField)
    {
        Name = name;
        Description = description;
        Type = type;
        NameMap = nameMap;
        LookupField = lookupField ?? typeof(T).GetProperties().FirstOrDefault() ?? throw new InvalidOperationException($"RecordType '{typeof(T).Name}' does not contain any properties");
    }

    public SharePointSchemaType Type { get; }

    public string Name { get; }

    public string Description { get; }

    public PropertyInfo LookupField { get; }

    /// <summary>
    /// Builder for property name → SharePoint column internal name mappings.
    /// </summary>
    public PropertyNameMapBuilder<T> NameMap { get; }

    public static SiteSchema<T> Create(IEnumerable<ISchemaConfiguration> configurations, string siteId)
    {
        var builder = new SiteSchemaBuilder<T>();

        var configuration = configurations.OfType<ISchemaConfiguration<T>>().FirstOrDefault();

        if (configuration == null)
        {
            throw new InvalidOperationException($"No schema configuration found for record type '{typeof(T).Name}'");
        }

        configuration?.Configure(builder);

        return builder.Build(siteId);
    }
}


public sealed class SiteSchemaBuilder<T> : ISiteSchemaBuilder<T> where T : class
{
    private PropertyInfo? _lookupField;
    private string? _name;
    private string? _description;
    private SharePointSchemaType _type = SharePointSchemaType.List;
    private readonly PropertyNameMapBuilder<T> _propertyMap = PropertyNameMap.For<T>();

    /// <summary>
    /// Sets the display name of the SharePoint content type or list.
    /// When omitted, the type name is split on camelCase boundaries (e.g. "PortalType" → "Portal Type").
    /// </summary>
    public ISiteSchemaBuilder<T> HasName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Optional description to set on the SharePoint content type or list.
    /// </summary>
    /// <param name="description"></param>
    /// <returns></returns>
    public ISiteSchemaBuilder<T> HasDescription(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _description = description;
        return this;
    }

    /// <summary>
    /// Maps a property to a specific SharePoint internal column name.
    /// </summary>
    public ISiteSchemaBuilder<T> MapProperty(Expression<Func<T, object?>> selector, string internalName)
    {
        _propertyMap.Map(selector, internalName);
        return this;
    }

    /// <summary>
    /// Sets the default naming strategy for properties that are not explicitly mapped.
    /// </summary>
    public ISiteSchemaBuilder<T> DefaultPropertyName(Func<PropertyInfo, string> defaultName)
    {
        _propertyMap.Default(defaultName);
        return this;
    }

    public ISiteSchemaBuilder<T> AsList(Expression<Func<T, object?>>? lookupFieldSelector = null)
    {
        _type = SharePointSchemaType.List;
        _lookupField = ExtractProperty(lookupFieldSelector) ?? typeof(T).GetProperties().FirstOrDefault(p => p.Name == "Title");
        return this;
    }

    public ISiteSchemaBuilder<T> AsFolder()
    {
        _type = SharePointSchemaType.Folder;
        return this;
    }

    public ISiteSchemaBuilder<T> AsDocument()
    {
        _type = SharePointSchemaType.Document;
        return this;
    }

    /// <summary>
    /// Builds the resulting <see cref="SiteSchema{T}"/> from this builder's state.
    /// </summary>
    internal SiteSchema<T> Build(string siteId)
    {
        var schema = new SiteSchema<T>(
            _name ?? typeof(T).GetDisplayName(),
            _description ?? typeof(T).GetDisplayName(),
            _type,
            _propertyMap,
            _lookupField);

        schema.NameMap.Build(siteId);

        return schema;
    }

    private static PropertyInfo? ExtractProperty(Expression<Func<T, object?>>? selector)
    {
        if (selector == null) return null;

        // unwrap convert (e.g. value types boxed to object)
        var body = selector.Body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert
            ? unary.Operand
            : selector.Body;

        return body is MemberExpression { Member: PropertyInfo property } ? property : null;
    }
}
public enum SharePointSchemaType
{
    Folder,
    List,
    Document
}