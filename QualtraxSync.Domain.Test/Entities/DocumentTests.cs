using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using System.Text;
using Xunit;

namespace QualtraxSync.Domain.Test.Entities;

public class DocumentTests
{
    [Fact]
    public void AddDocument_CreatesFirstRevision_WithCreatedEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "Procedure", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), null);
        Assert.Equal(document, Assert.Single(folder.Documents));
        var revision = Assert.Single(document.Revisions);

        Assert.Contains(revision.DomainEvents, e => e is RevisionCreatedNotification);
    }

    [Fact]
    public void Rename_ChangesName_AndRaisesRenamedEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var added = new Document(10, "OldName", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), null);

        var document = Assert.Single(folder.Documents);

        document.Rename("NewName", t0.AddMinutes(2));

        Assert.Equal("NewName", document.Name);

        var renamed = Assert.Single(document.DomainEvents.OfType<DocumentRenamedNotification>());
        Assert.Equal("OldName", renamed.OldName);
    }

    [Fact]
    public void Rename_SameName_DoesNotRaiseEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "Same", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), null);
        Assert.Equal(document, Assert.Single(folder.Documents));

        document.Rename("Same", t0.AddMinutes(2));

        Assert.DoesNotContain(document.DomainEvents, e => e is DocumentRenamedNotification);
    }

    [Fact]
    public void Move_RelocatesDocumentInCollections_AndRaisesMovedEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var source = new Folder(1, "Source", null, t0, t0);
        var target = new Folder(2, "Target", null, t0, t0);

        var document = new Document(10, "Doc", t0.DateTime, source, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), null);
        Assert.Equal(document, Assert.Single(source.Documents));

        document.Move(target, t0.AddMinutes(2));

        Assert.DoesNotContain(document, source.Documents);
        Assert.Contains(document, target.Documents);

        var moved = Assert.Single(document.DomainEvents.OfType<DocumentMovedNotification>());
        Assert.Equal(source, moved.OldParent);
    }

    [Fact]
    public void Remove_DeletesFromFolder_AndRaisesRemovedEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "Doc", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), null);
        Assert.Equal(document, Assert.Single(folder.Documents));

        document.Remove(t0.AddMinutes(2));

        Assert.Empty(folder.Documents);
        Assert.Contains(document.DomainEvents, e => e is DocumentRemovedNotification);
    }

    [Fact]
    public void Revise_AddsNewRevision_ArchivesPrevious_AndRaisesEvents()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);

        var document = new Document(10, "Doc", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), null);
        Assert.Equal(document, Assert.Single(folder.Documents));
        var rev1 = Assert.Single(document.Revisions);

        SaveAndClearEvents(document, rev1);

        document.Revise(2, true, "pdf", StreamOf("rev2"), t0.AddMinutes(2), null);

        Assert.Equal(2, document.Revisions.Count);
        Assert.NotNull(rev1.Archived);
        Assert.Contains(rev1.DomainEvents, e => e is RevisionArchivedNotification);

        var rev2 = document.Revisions.Last();
        Assert.Contains(rev2.DomainEvents, e => e is RevisionCreatedNotification);
    }

    private static void SaveAndClearEvents(params Entity[] entities)
    {
        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents.ToList())
            {
                entity.RemoveDomainEvent(domainEvent);
            }
        }
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private static Stream StreamOf(string text) =>
        new MemoryStream(Encoding.UTF8.GetBytes(text));
}
