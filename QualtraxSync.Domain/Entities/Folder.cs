using QualtraxSync.Domain.Base;
using System.Collections.Concurrent;

namespace QualtraxSync.Domain.Entities;

public class Folder : Entity<int>
{
    private readonly ConcurrentDictionary<Folder, byte> _children = new();
    private readonly ConcurrentDictionary<Document, byte> _documents = new();

    public static readonly Folder Retired = CreateDefault(-6, "Retired Documents");
    public static readonly Folder Unretired = CreateDefault(-8, "Unretired Documents");

    private Folder() { }
    public Folder(int id, string name, Folder? parent, DateTimeOffset created, DateTimeOffset modified)
    {
        Id = id;
        Name = name;
        Created = created == default ? new DateTimeOffset(new DateOnly(1900, 1, 1), TimeOnly.MinValue, TimeSpan.Zero) : created;
        Modified = modified == default ? created : modified;

        parent?.AddFolder(this, modified);

        AddDomainEvent(new FolderCreatedNotification(this));
    }

    public string Name { get; private set; } = null!;

    public Folder? Parent { get; private set; }

    public DateTimeOffset Created {  get; private set; }

    public DateTimeOffset Modified { get; private set; }

    public IEnumerable<Folder> Children => _children.Keys.AsEnumerable();

    public IEnumerable<Document> Documents => _documents.Keys.AsEnumerable();

    public void Rename(string name, DateTimeOffset modified)
    {

        if (Name == name) return;

        var oldName = Name;

        Name = name;
        Update(modified);

        if (DomainEvents.OfType<FolderRenamedNotification>().Any() == false)
        {
            AddDomainEvent(new FolderRenamedNotification(this, oldName));
        }
    }

    public void Move(Folder? to, DateTimeOffset modified)
    {
        if (Parent == to) return;

        if (Parent?._children.TryRemove(this, out _) == true)
        {
            Parent?.Update(modified);
        }

        if (DomainEvents.OfType<FolderMovedNotification>().Any() == false)
        {
            AddDomainEvent(new FolderMovedNotification(this, Parent));
        }

        Parent = to;
        Parent?.AddFolder(this, modified);
    }

    public void Remove(DateTimeOffset modified)
    {
        if (Parent?._children.TryRemove(this, out _) == false) return;

        Parent?.Update(modified);

        if (DomainEvents.OfType<FolderRemovedNotification>().Any() == false)
        {
            AddDomainEvent(new FolderRemovedNotification(this));
        }
    }

    /// <summary>
    /// Returns a unique list of all the lifecycle statuses of the documents in this folder and its children.
    /// </summary>
    public IEnumerable<Lifecycle?> Lifecycles(bool withLifecycle = false) => withLifecycle ? Lifecycles([]) : [null];

    public Folder Root() => Parent?.Root() ?? this;

    public IEnumerable<Folder> Hierachy()
    {
        yield return this;

        if (Parent != null)
        {
            foreach (var level in Parent.Hierachy())
            {
                yield return level;
            }
        }
    }

    public IEnumerable<Document> GetAllDocuments()
    {
        foreach (var document in _documents.Keys)
        {
            yield return document;
        }
        foreach (var child in _children.Keys)
        {
            foreach (var document in child.GetAllDocuments())
            {
                yield return document;
            }
        }
    }

    public bool Dependents() => _children.IsEmpty == false || _documents.IsEmpty == false;

    internal bool AddDocument(Document document, DateTimeOffset modified)
    {
        if (_documents.TryAdd(document, 0) == false) return false;

        Update(modified);

        if (DomainEvents.OfType<FolderRemovedNotification>().FirstOrDefault() is { } removed)
        {
            RemoveDomainEvent(removed);
        }

        return true;
    }

    internal bool RemoveDocument(Document document, DateTimeOffset modified)
    {
        if (_documents.TryRemove(document, out _))
        {
            Update(modified);
            return true;
        }

        return false;
    }

    internal bool MoveDocument(Document document, Folder to, DateTimeOffset modified)
    {
        if (!_documents.ContainsKey(document)) return false;

        RemoveDocument(document, modified);
        to.AddDocument(document, modified);

        if (_children.IsEmpty && _documents.IsEmpty)
        {
            Remove(modified);
        }

        return true;
    }


    internal bool AddFolder(Folder folder, DateTimeOffset modified)
    {
        if (!_children.TryAdd(folder, 0)) return false;

        folder.Parent = this;
        Update(modified);

        if (DomainEvents.OfType<FolderRemovedNotification>().FirstOrDefault() is { } removed)
        {
            RemoveDomainEvent(removed);
        }

        return true;
    }

    internal void Update(DateTimeOffset modified)
    {
        if (modified < Modified) return;

        Modified = modified;
        Parent?.Update(modified);

        if (DomainEvents.Any() == false) AddDomainEvent(new FolderUpdatedNotification(this, [..Lifecycles(true)]));
    }

    private IEnumerable<Lifecycle?> Lifecycles(HashSet<Lifecycle> found)
    {
        foreach (var lifecycle in Documents.SelectMany(d => d.Revisions).Select(r => r.Lifecycle(false)))
        {
            if (found.Add(lifecycle)) yield return lifecycle;
            if (found.Count == 3) yield break;
        }

        if (found.Count < 3)
        {
            foreach (var lifecycle in Children.SelectMany(c => c.Lifecycles(found)))
            {
                yield return lifecycle;
                if (found.Count == 3) yield break;
            }
        }
    }

    private static Folder CreateDefault(int id, string name)
    {
        var folder = new Folder(id, name, null, default, default);
        
        foreach(var notification in folder.DomainEvents.ToList())
        {
            folder.RemoveDomainEvent(notification);
        }

        return folder;
    }
}

public record FolderCreatedNotification(Folder Folder) : INotification;

public record FolderUpdatedNotification(Folder Folder, List<Lifecycle?> OldLifecycles) : INotification;

public record FolderRenamedNotification(Folder Folder, string OldName) : INotification;

public record FolderMovedNotification(Folder Folder, Folder? OldParent) : INotification;

public record FolderRemovedNotification(Folder Folder) : INotification;