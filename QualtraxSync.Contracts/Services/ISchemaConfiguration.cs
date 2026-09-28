using System.Linq.Expressions;
using System.Reflection;

namespace QualtraxSync.Contracts.Services;

public interface ISchemaConfiguration;

/// <summary>
/// Implement this interface on your metadata class (or a separate configuration class)
/// to define how it maps to a SharePoint list or content type.
/// </summary>
/// <typeparam name="T">The metadata model type.</typeparam>
public interface ISchemaConfiguration<T> : ISchemaConfiguration where T : class
{
    /// <summary>
    /// Configures the SharePoint schema for <typeparamref name="T"/> using the provided builder.
    /// </summary>
    void Configure(ISiteSchemaBuilder<T> builder);
}

public interface ISiteSchemaBuilder<T> where T : class
{
    ISiteSchemaBuilder<T> AsDocument();
    ISiteSchemaBuilder<T> AsFolder();
    ISiteSchemaBuilder<T> AsList(Expression<Func<T, object?>>? lookupFieldSelector = null);
    ISiteSchemaBuilder<T> DefaultPropertyName(Func<PropertyInfo, string> defaultName);
    ISiteSchemaBuilder<T> HasDescription(string description);
    ISiteSchemaBuilder<T> HasName(string name);
    ISiteSchemaBuilder<T> MapProperty(Expression<Func<T, object?>> selector, string internalName);
}