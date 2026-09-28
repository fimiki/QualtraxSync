using QualtraxSync.Domain.Base;

namespace QualtraxSync.Domain.Entities;

public class Document : Entity<int>
{
    private readonly List<Revision> _revisions = new();

    private Document() { }
    public Document(int id, string name, DateTimeOffset created, Folder parent, bool latest, int revision, string extension, Stream contents, DateTimeOffset published, DateTimeOffset? archived) 
    {
        Id = id;
        Name = name;
        Created = created == default ? new DateTimeOffset(new DateOnly(1900, 1, 1), TimeOnly.MinValue, TimeSpan.Zero) : created;
        Retired = parent == Folder.Retired;
        Folder = parent;
        Folder.AddDocument(this, published);

        Revise(revision, latest, extension, contents, published, archived);
    }

    public string Name { get; private set; } = "Default";

    public Folder Folder { get; private set; } = null!;

    public DateTimeOffset Created { get; private set; }

    /// <summary>
    /// Weather this document has been retired in Qualtrax system.  Set from SharePoint by inspecting the folder path for "Retired" folder.
    /// </summary>
    public bool Retired { get; private set; }

    public IReadOnlyList<Revision> Revisions => _revisions.AsReadOnly();

    public void Revise(int revision, bool latest, string extension, Stream contents, DateTimeOffset published, DateTimeOffset? archived)
    {
        if (_revisions.OrderByDescending(r => r.Id.Revision).FirstOrDefault(r => r.Id.Revision < revision) is Revision previous) 
        {
            if (previous.Archived.HasValue)
            {
                published = previous.Archived.Value;
            }
            else
            {
                previous.Archive(published);
            }
        }

        _revisions.Add(new Revision(this, contents, revision, extension, published, archived));

        if (latest)
        {
            Retired = archived.HasValue;
        }

        Folder.Update(published);
    }

    public void Retire(DateTimeOffset retired)
    {
        if (Retired) return;

        Retired = true;

        foreach (var revision in _revisions)
        {
            revision.Archive(retired);
        }
    }

    public void Unretire(DateTimeOffset unretired)
    {
        if (Retired == false) return;

        Retired = false;

        _revisions.OrderByDescending(r => r.Id).First().Reactivate(unretired);
    }

    public void Rename(string name, DateTimeOffset modified)
    {
        if (Name == name) return;

        if (DomainEvents.OfType<DocumentRenamedNotification>().Any() == false)
        {
            AddDomainEvent(new DocumentRenamedNotification(this, Name));
        }
        
        Name = name;

        Folder.Update(modified);
    }

    public void Move(Folder to, DateTimeOffset modified)
    {
        var oldParent = Folder;

        if (Folder.MoveDocument(this, to, modified) && DomainEvents.OfType<DocumentMovedNotification>().Any() == false)
        {
            AddDomainEvent(new DocumentMovedNotification(this, oldParent));
        }

        if (to.Documents.Contains(this))
        {
            Folder = to;
        }      
    }

    public void Remove(DateTimeOffset modified)
    {
        if (Folder.RemoveDocument(this, modified) && DomainEvents.OfType<DocumentRemovedNotification>().Any() == false)
        {
            AddDomainEvent(new DocumentRemovedNotification(this));
        }
    }

    internal int Iteration(bool updated = true)
    {
        var folder = updated ? Folder : DomainEvents.OfType<DocumentMovedNotification>().FirstOrDefault()?.OldParent ?? Folder;
        var iteration = 0;

        foreach (var document in folder.Documents.Where(d => d.Name == Name).OrderBy(d => d.Revisions.OrderBy(r => r.Id).First().Published).ThenBy(d => d.Id))
        {
            iteration++;
            if (document == this)
            {
                return iteration;
            }
        }

        return iteration;
    }
}

public record DocumentRenamedNotification(Document Document, string OldName) : INotification;

public record DocumentMovedNotification(Document Document, Folder OldParent) : INotification;

public record DocumentRemovedNotification(Document Document) : INotification;