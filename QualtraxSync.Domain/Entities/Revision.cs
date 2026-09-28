using QualtraxSync.Domain.Base;

namespace QualtraxSync.Domain.Entities;

public class Revision : Entity<RevisionKey>
{
    private Revision() { }
    internal Revision(Document document, Stream contents, int id, string extension, DateTimeOffset published, DateTimeOffset? archived)
    {
        Id = new RevisionKey(document.Id, id);
        Extension = extension;
        Published = published;
        Archived = archived;
        Document = document;
        Document.Folder.Update(published);

        AddDomainEvent(new RevisionCreatedNotification(this, contents));
    }

    /// <summary>
    /// The file extension of the revision, e.g., ".pdf", ".docx", etc.
    /// </summary>
    public string Extension { get; private set; } = string.Empty;

    /// <summary>
    /// When this revision was published.
    /// </summary>
    public DateTimeOffset Published { get; private set; }

    /// <summary>
    /// When this revision was archived, if applicable.
    /// </summary>
    public DateTimeOffset? Archived { get; private set; }
    
    /// <summary>
    /// The document to which this revision belongs.
    /// </summary>
    public Document Document { get; private set; } = null!;


    public string Name(bool updated = true)
    {
        var title = updated ? Document.Name : Document.DomainEvents.OfType<DocumentRenamedNotification>().LastOrDefault()?.OldName ?? Document.Name;
        var archived = updated ? Archived : DomainEvents.OfType<RevisionArchivedNotification>().Any() ? null : DomainEvents.OfType<RevisionReactivatedNotification>().LastOrDefault()?.PreviousArchived ?? Archived;
        var iteration = Document.Iteration(updated) > 1 ? $" ({Document.Iteration(updated)})" : string.Empty;

        var value = $"{title}{iteration} - {Id.Revision:D3} - {Published.ToLocalTime():yyyy-MM-dd}";

        if (archived.HasValue)
        {
            value += $" through {archived.Value.ToLocalTime():yyyy-MM-dd}";
        }

        return value += $".{Extension}";
    }

    public Lifecycle Lifecycle(bool updated = true)
    {
        // infer the existing lifecycle state based on unprocessed domain events
        if (updated == false)
        {
            if (Document.Revisions.Any(r => r.DomainEvents.OfType<RevisionReactivatedNotification>().Any())) return Entities.Lifecycle.Retired;
            if (Document.Retired && Document.Revisions.Any(r => r != this && r.DomainEvents.OfType<RevisionArchivedNotification>().Any())) return Entities.Lifecycle.Archived;
            if (DomainEvents.OfType<RevisionArchivedNotification>().Any()) return Entities.Lifecycle.Current;
        }

        if (Document.Retired) return Entities.Lifecycle.Retired;
        if (Archived.HasValue) return Entities.Lifecycle.Archived;

        return Entities.Lifecycle.Current;
    }

    internal void Archive(DateTimeOffset archived)
    {
        if (Archived.HasValue) return;

        Archived = archived;

        if (DomainEvents.OfType<RevisionCreatedNotification>().Any() == false)
        {
            AddDomainEvent(new RevisionArchivedNotification(this));
        }

        Document.Folder.Update(archived);
    }

    internal void Reactivate(DateTimeOffset published)
    {
        if (Archived.HasValue == false) return;

        var previous = Archived;

        Archived = null;

        if (DomainEvents.OfType<RevisionCreatedNotification>().Any() == false)
        {
            AddDomainEvent(new RevisionReactivatedNotification(this, previous));
        }

        Document.Folder.Update(published);
    }
}

public readonly struct RevisionKey(int document, int revision) : IComparable<RevisionKey>, IEquatable<RevisionKey>
{
    public int Document { get; } = document;
    public int Revision { get; } = revision;

    public override string ToString() => $"{Document}-{Revision:D3}";

    public int CompareTo(RevisionKey other)
    {
        var documentComparison = Document.CompareTo(other.Document);
        if (documentComparison != 0) return documentComparison;
        return Revision.CompareTo(other.Revision);
    }

    public bool Equals(RevisionKey other) => Document == other.Document && Revision == other.Revision;

    public override bool Equals(object? obj) => obj is RevisionKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Document, Revision);

    public static bool operator ==(RevisionKey left, RevisionKey right) => left.Equals(right);
    public static bool operator !=(RevisionKey left, RevisionKey right) => !left.Equals(right);
    public static bool operator <(RevisionKey left, RevisionKey right) => left.CompareTo(right) < 0;
    public static bool operator <=(RevisionKey left, RevisionKey right) => left.CompareTo(right) <= 0;
    public static bool operator >(RevisionKey left, RevisionKey right) => left.CompareTo(right) > 0;
    public static bool operator >=(RevisionKey left, RevisionKey right) => left.CompareTo(right) >= 0;
}

public record RevisionCreatedNotification(Revision Revision, Stream Contents) : INotification;

public record RevisionArchivedNotification(Revision Revision) : INotification;

public record RevisionReactivatedNotification(Revision Revision, DateTimeOffset? PreviousArchived = null) : INotification;