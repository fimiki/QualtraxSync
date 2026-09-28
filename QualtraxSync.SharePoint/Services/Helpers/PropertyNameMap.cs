using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace QualtraxSync.SharePoint.Configuration.Helpers;


/// <summary>
/// Holds a mapping of <see cref="PropertyInfo"/> to internal name strings for a given type.
/// </summary>
/// 
public interface IPropertyNameMap
{
    string GetName(PropertyInfo property);
}

public sealed class PropertyNameMap<T>(Dictionary<PropertyInfo, string> map, Func<PropertyInfo, string> defaultName) : IPropertyNameMap
{
    /// <summary>
    /// Returns the mapped internal name for the given <see cref="PropertyInfo"/>,
    /// or the default name if no mapping exists.
    /// </summary>
    public string GetName(PropertyInfo property)
    {
        if (map.TryGetValue(property, out var name))
        {
            return name;
        }

        // Fall back to structural match in case the PropertyInfo instance differs
        // (e.g. ReflectedType vs DeclaringType producing different instances)
        var match = map.Keys.FirstOrDefault(p =>
            p.Module == property.Module &&
            p.MetadataToken == property.MetadataToken);

        return match is not null ? map[match] : defaultName(property);
    }
}

/// <summary>
/// Fluent builder for constructing a <see cref="PropertyNameMap{T}"/>.
/// </summary>
/// 
public sealed class PropertyNameMapBuilder<T>
{
    private readonly Dictionary<PropertyInfo, string> _map = [];
    private Func<PropertyInfo, string> _default = property => property.Name;

    /// <summary>
    /// Maps a property selected by <paramref name="selector"/> to the given <paramref name="internalName"/>.
    /// </summary>
    public PropertyNameMapBuilder<T> Map(Expression<Func<T, object?>> selector, string internalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalName);

        var property = ExtractProperty(selector)
            ?? throw new ArgumentException($"Expression does not refer to a property of '{typeof(T).Name}'.", nameof(selector));

        _map[property] = internalName;
        return this;
    }

    public PropertyNameMapBuilder<T> Default(Func<PropertyInfo, string> defaultName)
    {
        _default = defaultName;
        return this;
    }

    /// <summary>
    /// Builds and returns the configured <see cref="PropertyNameMap{T}"/>.
    /// </summary>
    public void Build(string siteId)
    {
        var map = new PropertyNameMap<T>(_map, _default);
        var key = new TypeKey(typeof(T), siteId);
        PropertyNameMap.NameMaps.TryAdd(key, map);
    }

    private static PropertyInfo? ExtractProperty(Expression<Func<T, object?>> selector)
    {
        // unwrap convert (e.g. value types boxed to object)
        var body = selector.Body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert
            ? unary.Operand
            : selector.Body;

        return body is MemberExpression { Member: PropertyInfo property } ? property : null;
    }
}

/// <summary>
/// Static entry point for creating a <see cref="PropertyNameMapBuilder{T}"/>.
/// </summary>
public static class PropertyNameMap
{
    internal static ConcurrentDictionary<TypeKey, IPropertyNameMap> NameMaps = [];

    /// <summary>
    /// Returns a new <see cref="PropertyNameMapBuilder{T}"/> for the specified type.
    /// </summary>
    public static PropertyNameMapBuilder<T> For<T>() => new();
}

public record struct TypeKey(Type Type, string NameSpace);

/// <summary>
/// Extension methods for <see cref="PropertyInfo"/> to resolve mapped internal names.
/// </summary>
public static partial class PropertyNameMapExtensions
{

    /// <summary>
    /// Returns the mapped internal name for this <see cref="PropertyInfo"/> using the map for type <typeparamref name="T"/>,
    /// or simply the property name if no mapping exists.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="property"></param>
    /// <returns></returns>
    public static string GetMappedName(this PropertyInfo property, string nameSpace)
    {
        if (property.ReflectedType == null)
        {
            return property.Name;
        }

        var key = new TypeKey(property.ReflectedType, nameSpace);

        if (PropertyNameMap.NameMaps.TryGetValue(key, out var map) is false)
        {
            return property.Name;
        }

        return map.GetName(property);
    }

    public static string GetDisplayName(this Type type) =>
        SplitCamelCaseRegex().Replace(type.Name, "$1 $2").Replace('_', ' ');

    public static string GetDisplayName(this PropertyInfo property) =>
        SplitCamelCaseRegex().Replace(property.Name, "$1 $2").Replace('_', ' ');

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex SplitCamelCaseRegex();
}