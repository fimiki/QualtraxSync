using QualtraxSync.Contracts.Services;
using QualtraxSync.SharePoint.Configuration.Helpers;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace QualtraxSync.SharePoint.Services.Helpers;

public static class FieldValueDeserialization
{
    public static T GetMetadata<T>(this IDictionary<string, object> fields, IListService listService, string siteId) where T : class
    {
        var result = new Dictionary<string, object?>();

        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            result[property.Name] = fields.GetValue(property, listService, siteId);
        }

        var json = JsonSerializer.Serialize(result);

        return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException($"Failed to deserialize field values into {typeof(T).Name}.");
    }

    public static object? GetValue(this IDictionary<string, object> fields, PropertyInfo property, IListService listService, string siteId)
    {
        var internalName = property.GetMappedName(siteId);
        var isArray = property.PropertyType.IsArray;
        var propertyType = isArray ? property.PropertyType.GetElementType()! : property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (listService.GetKey(siteId, underlyingType) is ListKey lookup)
        {
            var lookupKey = internalName + "LookupId";
            if (!fields.TryGetValue(lookupKey, out var lookupRaw)) return null;
            var lookupId = lookupRaw?.ToString();
            if (lookupId == null) return null;

            if (isArray)
            {
                var ids = lookupId.Split(";#", StringSplitOptions.RemoveEmptyEntries);
                var lookupArray = Array.CreateInstance(underlyingType, ids.Length);
                for (var i = 0; i < ids.Length; i++)
                {
                    var instance = listService.GetValue(siteId, ids[i], underlyingType);
                    lookupArray.SetValue(instance, i);
                }
                return lookupArray;
            }
            else
            {
                return listService.GetValue(siteId, lookupId, underlyingType);
            }
        }
        else
        {
            if (!fields.TryGetValue(internalName, out var raw) || raw == null) return null;

            return underlyingType switch
            {
                var t when t == typeof(DateOnly) => ParseDateOnly(raw),
                var t when t == typeof(DateTime) => ParseDateTime(raw),
                var t when t == typeof(DateTimeOffset) => ParseDateTimeOffset(raw),
                var t when t == typeof(int) => ParseInteger(raw),
                var t when t == typeof(double) => ParseDouble(raw),
                var t when t == typeof(bool) => ParseBool(raw),
                var t when t == typeof(Uri) => ParseUri(raw),
                _ => raw is JsonElement je && je.ValueKind == JsonValueKind.String ? je.GetString() : raw.ToString()
            };
        }
    }

    private static DateOnly? ParseDateOnly(object raw)
    {
        if (raw is JsonElement { ValueKind: JsonValueKind.String } je && DateOnly.TryParse(je.GetString(), out var d)) return d;
        return DateOnly.TryParse(raw.ToString(), out var d2) ? d2 : null;
    }

    private static DateTime? ParseDateTime(object raw)
    {
        DateTime? result = null;

        if (raw is JsonElement { ValueKind: JsonValueKind.String } je && DateTime.TryParse(je.GetString(), out var dt))
        {
            result = dt;
        }
        else if (DateTime.TryParse(raw.ToString(), out var dt2))
        {
            result = dt2;
        }

        if (result.HasValue)
        {
            return result.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(result.Value, DateTimeKind.Utc) : result;
        }

        return result;
    }

    private static DateTimeOffset? ParseDateTimeOffset(object raw)
    {
        DateTimeOffset? result = null;

        if (raw is JsonElement { ValueKind: JsonValueKind.String } je && DateTimeOffset.TryParse(je.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            result = dt;
        }
        else if (DateTimeOffset.TryParse(raw.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt2))
        {
            result = dt2;
        }

        return result;
    } 

    private static bool? ParseBool(object raw)
    {
        if (raw is JsonElement je)
            return je.ValueKind == JsonValueKind.True || je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var b) && b;
        return bool.TryParse(raw.ToString(), out var b2) ? b2 : null;
    }

    private static int? ParseInteger(object raw)
    {
        if (raw is JsonElement je)
            return je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var i) ? i : null;
        return double.TryParse(raw.ToString(), out var i2) ? (int)i2 : null;
    }

    private static double? ParseDouble(object raw)
    {
        if (raw is JsonElement je)
            return je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var d) ? d : null;
        return double.TryParse(raw.ToString(), out var d2) ? d2 : null;
    }

    private static Uri? ParseUri(object raw)
    {
        string? url = null;
        if (raw is JsonElement je)
            url = je.ValueKind == JsonValueKind.Object && je.TryGetProperty("Url", out var urlProp) ? urlProp.GetString() : je.GetString();
        else if (raw is Dictionary<string, object> dict && dict.TryGetValue("Url", out var urlObj))
            url = urlObj?.ToString();
        else
            url = raw.ToString();

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }
}