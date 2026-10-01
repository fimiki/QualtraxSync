using Microsoft.Extensions.Options;
using QualtraxSync.Domain.Entities;

namespace QualtraxSync.Persistence.Services;

/// <summary>
/// Computes the SharePoint path for folders and revisions, honoring lifecycle folders and root folder mappings.
/// This is a persistence concern rather than a domain concern, so it lives here instead of on the domain entities.
/// </summary>
public class PathService(IOptions<Options> options)
{
    /// <summary>
    /// The path a folder occupies (or occupied, if <paramref name="updated"/> is false) in SharePoint, optionally nested under a lifecycle folder.
    /// </summary>
    public string Path(Folder folder, bool updated = true, Lifecycle? lifecycle = null)
    {
        var path = FolderPath(folder, updated, lifecycle);
        var rootFolder = options.Value.ResolveRootFolder(folder.Root().Name);

        return rootFolder == null ? path : $"{rootFolder}/{path}";
    }

    /// <summary>
    /// The folder path a revision occupies (or occupied, if <paramref name="updated"/> is false) in SharePoint, taking into account
    /// the revision's lifecycle folder (if lifecycle folders are enabled).
    /// </summary>
    public string Path(Revision revision, bool updated = true)
    {
        var lifecycle = options.Value.Lifecycle.UseFolders ? revision.Lifecycle(updated) : null;
        var parent = updated ? revision.Document.Folder : revision.Document.DomainEvents.OfType<DocumentMovedNotification>().FirstOrDefault()?.OldParent ?? revision.Document.Folder;

        return Path(parent, updated, lifecycle) + '/' + revision.Name(updated);
    }

    /// <summary>
    /// Returns all possible lifecycle paths for a given original path
    /// </summary>
    /// <param name="original"></param>
    /// <returns>All possible lifecycle paths</returns>
    public IEnumerable<string> LifecyclePaths(string original)
    {
        if (string.IsNullOrWhiteSpace(original)) yield break;

        var components = original.Split('/');
        var rootFolder = options.Value.ResolveRootFolder(components[0]);
        var baseFolders = rootFolder == null ? [components[0]] : components[..1];

        if (components.Length == baseFolders.Length)
        {
            foreach (var lifecycle in LifecycleNames())
            {
                yield return string.Concat(original, '/', lifecycle);
            }

            yield break;
        }

        var hasLifecycle = LifecycleNames().Contains(components[baseFolders.Length]);

        foreach (var lifecycle in LifecycleNames())
        {
            var lifecyclePath = string.Concat(string.Join('/', baseFolders), '/', lifecycle, '/', string.Join('/', components[(baseFolders.Length + (hasLifecycle ? 1 : 0))..]));

            yield return lifecyclePath;
        }
    }

    internal static IEnumerable<string> LifecycleNames() => [Lifecycle.Current.Name, Lifecycle.Archived.Name, Lifecycle.Retired.Name];

    private static string FolderPath(Folder folder, bool updated, Lifecycle? lifecycle)
    {
        var parent = updated ? folder.Parent : folder.DomainEvents.OfType<FolderMovedNotification>().FirstOrDefault()?.OldParent ?? folder.Parent;
        var name = updated ? folder.Name : folder.DomainEvents.OfType<FolderRenamedNotification>().FirstOrDefault()?.OldName ?? folder.Name;

        if (parent != null)
        {
            return string.Concat(FolderPath(parent, updated, lifecycle), "/", name);
        }

        return lifecycle != null ? string.Concat(name, "/", lifecycle.Name) : name;
    }
}
