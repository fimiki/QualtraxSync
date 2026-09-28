using QualtraxSync.SharePoint.Configuration.Helpers;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using QualtraxSync.Contracts.Services;

namespace QualtraxSync.SharePoint.Services.Helpers;


public static class FieldValueSerialization
{
    public static async Task<Dictionary<string, object?>> GetFieldValuesAsync<T>(this T metadata, string siteId, IListService listService) where T : class
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var values = await Task.WhenAll(properties.Select(property => metadata.GetFieldValueAsync(property, siteId, listService)).Where(task => task != default));

        return values.Where(kv => kv.Key != null).ToDictionary();
    }

    public static async Task<KeyValuePair<string, object?>> GetFieldValueAsync<T>(this T metadata, PropertyInfo property, string siteId, IListService listService) where T : class
    {
        var readOnly = property.GetCustomAttribute<ReadOnlyAttribute>()?.IsReadOnly ?? false;
        if (readOnly) return default;

        var value = property.GetValue(metadata);
        var internalName = property.GetMappedName(siteId);
        var isArray = property.PropertyType.IsArray;
        var propertyType = isArray ? property.PropertyType.GetElementType()! : property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (listService.GetKey(siteId, underlyingType) is ListKey lookup)
        {
            var lookupName = internalName + "LookupId"; // required by graph API but removed for SharePoint API
            var lookupIdMethod = typeof(IListService).GetMethod(nameof(IListService.GetIdAsync))?.MakeGenericMethod(underlyingType);

            if (value == null)
            {
                return new KeyValuePair<string, object?>(lookupName, null);
            }
            else if (value is IEnumerable enumerable && value is not string)
            {
                var lookupIds = new List<string>();
                foreach (var item in enumerable)
                {
                    var lookupId = (Task<string>?)lookupIdMethod?.Invoke(listService, [siteId, item]);
                    if (lookupId != null) lookupIds.Add(await lookupId);
                }
                if (lookupIds.Count != 0) return new KeyValuePair<string, object?>(lookupName, string.Join(";#", lookupIds));
            }
            else
            {
                var lookupId = (Task<string>?)lookupIdMethod?.Invoke(listService, [siteId, value]);
                if (lookupId != null) return new KeyValuePair<string, object?>(lookupName, await lookupId);
            }
        }
        else if (value == null)
        {
            return new KeyValuePair<string, object?>(internalName, null);
        }

        var serialized = underlyingType switch
        {
            var t when t == typeof(DateOnly) => MapDateOnlyValue((DateOnly)value),
            var t when t == typeof(DateTime) => MapDateTimeValue((DateTime)value),
            var t when t == typeof(DateTimeOffset) => MapDateTimeOffsetValue((DateTimeOffset)value),
            var t when t == typeof(int) => value,
            var t when t == typeof(bool) => value,
            var t when t == typeof(string) || t.IsValueType => value.ToString(),
            var t when t == typeof(Uri) => await MapUriValueAsync(value, property, metadata, siteId, listService),
            _ => JsonSerializer.Serialize(value)
        };

        return new KeyValuePair<string, object?>(internalName, serialized);
    }

    private static string MapDateOnlyValue(DateOnly value, bool forGraph = true) => MapDateTimeValue(value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local), forGraph);

    private static string MapDateTimeValue(DateTime value, bool forGraph = true)
    {
        return forGraph ? value.ToUniversalTime().ToString("o") : value.ToLocalTime().ToString("g");
    }

    private static string MapDateTimeOffsetValue(DateTimeOffset value, bool forGraph = true)
    {
        return forGraph ? value.ToUniversalTime().ToString("o") : value.ToLocalTime().ToString("g");
    }

    private static async Task<object> MapUriValueAsync<T>(this object value, PropertyInfo property, T metadata, string siteId, IListService listService, bool forGraph = true) where T : class
    {
        var url = value.ToString();
        var description = await property.GetUriDescriptionAsync(metadata, siteId, listService);

        return forGraph ? new Dictionary<string, object?> { { "Description", description }, { "Url", url } } : $"{url}, {description}";
    }

    private static async Task<string> GetUriDescriptionAsync<T>(this PropertyInfo property, T metadata, string siteId, IListService listService) where T : class
    {
        var allProperties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        if (property.GetCustomAttribute<DisplayAttribute>() is DisplayAttribute displayAttribute && displayAttribute.Name != null)
        {
            if (allProperties.FirstOrDefault(p => p.Name == displayAttribute.Name) is PropertyInfo displayProperty)
            {
                var displayValue = await metadata.GetFieldValueAsync(displayProperty, siteId, listService);

                if (displayValue.Value?.ToString() is string displayString)
                { 
                    return displayString;
                }
            }

            return displayAttribute.Name;
        }

        return "View";
    }
}
