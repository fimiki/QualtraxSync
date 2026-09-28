using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Domain.Entities;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace QualtraxSync.Persistence.Services.Materializers;

public class FolderMaterializer(
    EntityStore<Folder, int> entities, 
    FolderRoots roots,
    PathService pathService,
    IOptions<Options> options,
    IMetadataService metadataService) : MaterializerBase
{
    private int _nextOrphanId = int.MinValue;

    public Folder Materialize(KeyValuePair<string, Models.Folder> item, Dictionary<string, Models.Folder> folders)
    {
        if (entities.Get(item.Value.Id) is { } existing)
        {
            return existing;
        }

        var path = item.Key;
        var folder = item.Value;

        Folder? parent = null;

        var (parentPath, name) = SplitPath(path);

        if (string.IsNullOrEmpty(parentPath) == false)
        {
            parent = GetParent(parentPath, folders);

            if (parent == null)
            {
                if (IsRoot(parentPath, out var rootName))
                {
                    name = rootName;
                }
                else
                {
                    throw new InvalidOperationException($"Folder {name} {folder.Id} has a parent parentPath of {parentPath} that is not a root folder and could not be found in the list of folders");
                }
            }
        }

        var materialized = (Folder)Activator.CreateInstance(typeof(Folder), nonPublic: true)!;

        SetProperty(materialized, nameof(Folder.Id), folder.Id);
        SetProperty(materialized, nameof(Folder.Name), name);
        SetProperty(materialized, nameof(Folder.Parent), parent);
        SetProperty(materialized, nameof(Folder.Created), folder.Created);
        SetProperty(materialized, nameof(Folder.Modified), folder.Modified);

        if (parent == null)
        {
            roots.Add(materialized);
        }

        return entities.Add(materialized);
    }

    public void AddToParent()
    {
        foreach (var group in entities.GetAll().GroupBy(e => e.Parent))
        {
            var parent = group.Key;
            if (parent == null) continue;

            var children = new ConcurrentDictionary<Folder, byte>(group.Select(e => new KeyValuePair<Folder, byte>(e, 0)));

            SetProperty(parent, '_' + nameof(Folder.Children).ToLower(), children);
        }
    }

    public Folder? GetParent(string parentPath, Dictionary<string, Models.Folder> folders)
    {
        // no parent path - this must be a root folder
        if (string.IsNullOrEmpty(parentPath)) return null;

        // see if the parent is in the list of folders that have been stamped with the Qualtrax Folder content type
        if (folders.TryGetValue(parentPath, out var parentFolder)) return Materialize(KeyValuePair.Create(parentPath, parentFolder), folders);

        // see if the parent is a root folder
        if (IsRoot(parentPath, out var rootName))
        {
            if (folders.TryGetValue(rootName, out var rootFolder)) return Materialize(KeyValuePair.Create(rootName, rootFolder), folders);

            var rootPath = parentPath.Split('/');
            if (rootPath.Length > 1 && folders.TryGetValue(string.Join('/', rootPath.Take(2)), out var mappedRoot)) return Materialize(KeyValuePair.Create(rootName, mappedRoot), folders);
            
            return null;
        }

        // see if the parent is in a lifecycle folder path that hasn't yet been stamped with the Qualtrax Folder content type
        foreach (var lifecyclePath in pathService.LifecyclePaths(parentPath))
        {
            if (folders.TryGetValue(lifecyclePath, out var lifecycleFolder))
            {
                var found = Materialize(KeyValuePair.Create(lifecyclePath, lifecycleFolder), folders);
                if (found != null)
                {
                    // stamp the folder with the Qualtrax Folder content type so it can quickly be found in future syncs
                    var metadata = Models.Folder.FromDomainEntity(found);
                    metadataService.UpdateAsync(options.Value.Site.DriveId, parentPath, metadata).Wait();
                    folders.Add(parentPath, metadata);
                    return found;
                }
            }
        }

        // create an orphaned folder that will tracked by the domain and replaced by the correct one when it is synced from Qualtrax
        return CreateTemp(parentPath, folders);
    }

    private Folder CreateTemp(string path, Dictionary<string, Models.Folder> folders)
    {
        var (parentPath, name) = SplitPath(path);
        var orphan = new Folder(_nextOrphanId++, name, GetParent(parentPath, folders), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        entities.Add(orphan);
        folders.Add(path, Models.Folder.FromDomainEntity(orphan));

        return orphan;
    }

    internal Folder? ToRootEntity(Dictionary<string, Models.Folder> folders, string path)
    {
        var (parentPath, name) = SplitPath(path);

        if (IsLifecycle(name) && folders.TryGetValue(parentPath, out var rootFolder))
        {
            return Materialize(KeyValuePair.Create(parentPath, rootFolder), folders);
        }

        return null;
    }

    /// <summary>
    /// Determines if the given path is a root folder path regardless of weather the path contains a lifecycle folder or mapped root
    /// </summary>
    private bool IsRoot(string path, [NotNullWhen(true)] out string? rootName)
    {
        if (string.IsNullOrEmpty(path)) 
        {
            rootName = null;
            return false;
        }

        var (parentPath, name) = SplitPath(path);
        
        // handles paths of "/root"
        if (string.IsNullOrEmpty(parentPath))
        {
            rootName = name;
            return true;
        }

        var components = parentPath.Split('/');

        if (components.Length > 2)
        {
            rootName = null;
            return false;
        }
        if (components.Length == 1)
        {
            // handles paths of "/root/lifecycle" and "/mappedRoot/root"
            if (IsLifecycle(name))
            {
                rootName = components[0];
                return true;
            }

            if (options.Value.ResolveRootFolder(name) == components[0])
            {
                rootName = name;
                return true;
            }

            rootName = null;
            return false;
        }

        // handles paths of "/mappedRoot/root/lifecycle"
        if (IsLifecycle(name) && options.Value.ResolveRootFolder(components[1]) == components[0])
        {
            rootName = components[1];
            return true;
        }

        rootName = null;
        return false;
    }

    private static bool IsLifecycle(string name) => PathService.LifecycleNames().Contains(name);
}

public class FolderRoots
{
    private readonly ConcurrentDictionary<int, byte> _roots = new();

    public IEnumerable<Folder> GetAll(EntityStore<Folder, int> entities)
    {
        foreach (var folderId in _roots.Keys)
        {
            if (entities.Get(folderId) is { } folder)
            {
                yield return folder;
            }
        }
    }

    public void Add(Folder folder)
    {
        if (folder.Parent != null) throw new InvalidOperationException($"Folder {folder.Name} {folder.Id} is not a root folder");
        _roots.TryAdd(folder.Id, 0);
    }

    public void Remove(Folder folder)
    {
        if (folder.Parent != null) throw new InvalidOperationException($"Folder {folder.Name} {folder.Id} is not a root folder");
        _roots.TryRemove(folder.Id, out _);
    }

    public void Refresh(Folder folder)
    {
        if (folder.Parent == null)
        {
            _roots.TryAdd(folder.Id, 0);
        }
        else
        {
            _roots.TryRemove(folder.Id, out _);
        }
    }
}