using Microsoft.Graph.Models;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using QualtraxSync.SharePoint.Configuration.Helpers;
using QualtraxSync.Contracts.Services;

namespace QualtraxSync.SharePoint.Services.Helpers;


public static class ColumnDefinitionMapper
{
    public static ColumnDefinition CreateColumnDefinition(this PropertyInfo property, string siteId, string groupName, IListService listService)
    {
        ColumnDefinition column;

        var isArray = property.PropertyType.IsArray;
        var propertyType = isArray ? property.PropertyType.GetElementType()! : property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (listService.GetKey(siteId, underlyingType) is ListKey lookup)
        {
            column = new ColumnDefinition
            {
                Lookup = new LookupColumn
                {
                    ListId              = lookup.ListId,
                    ColumnName          = lookup.FieldName,
                    AllowMultipleValues = isArray
                }
            };
        }
        else
        {
            column = propertyType switch
            {
                var t when t == typeof(string) => new ColumnDefinition
                {
                    Text = new TextColumn { MaxLength = isArray ? 50000 : 255, AllowMultipleLines = isArray }
                },
                var t when t == typeof(bool) || t == typeof(bool?) => new ColumnDefinition
                {
                    Boolean  = new BooleanColumn()
                },
                var t when t == typeof(int) || t == typeof(int?) => new ColumnDefinition
                {
                    Number = new NumberColumn { DisplayAs = "number", DecimalPlaces = "none" }
                },
                var t when t == typeof(DateTime) || t == typeof(DateTime?) => new ColumnDefinition
                {
                    DateTime = new DateTimeColumn { Format = "dateTime" }
                },
                var t when t == typeof(DateOnly) || t == typeof(DateOnly?) => new ColumnDefinition
                {
                    DateTime = new DateTimeColumn { Format = "dateOnly" }
                },
                var t when t == typeof(DateTimeOffset) || t == typeof(DateTimeOffset?) => new ColumnDefinition
                {
                    DateTime = new DateTimeColumn { Format = "dateTime" }
                },
                var t when t == typeof(Uri) => new ColumnDefinition
                {
                    HyperlinkOrPicture = new HyperlinkOrPictureColumn { IsPicture = false }
                },
                _ => new ColumnDefinition
                {
                    Text = new TextColumn { MaxLength = isArray ? 50000 : 255, AllowMultipleLines = isArray }
                }
            };
        }

        column.Name        = property.GetMappedName(siteId);
        column.DisplayName = property.GetDisplayName();
        column.Description = property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
        column.Indexed     = property.GetCustomAttribute<KeyAttribute>() != null;
        column.Required    = new NullabilityInfoContext().Create(property).ReadState == NullabilityState.NotNull;
        column.ReadOnly    = property.GetCustomAttribute<ReadOnlyAttribute>()?.IsReadOnly ?? false;
        column.ColumnGroup = groupName;

        return column;
    }
}
