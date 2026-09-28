namespace QualtraxSync.Persistence.Services.Materializers;

public abstract class MaterializerBase
{
    /// <summary>
    /// Determines if the given path indicates a retired entity based on its structure.  Obviously, this only works if we're using lifecycle folders, but since the retired status only matters when using lifecycle folders, that's OK!
    /// </summary>
    protected static bool IsRetired(string path)
    {
        if (path.StartsWith(Domain.Entities.Folder.Retired.Name)) return true;
        if (path[(path.IndexOf('/') + 1)..].StartsWith(Domain.Entities.Lifecycle.Retired.Name)) return true;

        return false;
    }

    /// <summary>
    /// Helper method to set a property value on an instance of a class using reflection. This method is useful for setting properties that may not be publicly accessible or when the property name is determined at runtime.
    /// </summary>
    protected static void SetProperty<T>(T instance, string propertyName, object? value) where T : notnull
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        if (typeof(T).GetProperty(propertyName, flags) is { } property)
        {
            property.SetValue(instance, value);
            return;
        }

        // backing collections are exposed as read only properties, so fall back to the underlying field
        var field = typeof(T).GetField(propertyName, flags)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on type '{typeof(T).Name}'.");

        field.SetValue(instance, value);
    }

    /// <summary>
    /// Extracts the parent path and the name from a given path string.
    /// </summary>
    protected static (string ParentPath, string Name) SplitPath(string path)
    {
        var trimmed = path.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');
        var hasParent = lastSlash > 0;

        return hasParent
            ? (trimmed[..lastSlash], trimmed[(lastSlash + 1)..])
            : (string.Empty, trimmed);
    }
}