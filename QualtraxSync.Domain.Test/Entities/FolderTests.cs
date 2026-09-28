using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using System.Text;
using Xunit;

namespace QualtraxSync.Domain.Test.Entities;

public class FolderTests
{
    [Fact]
    public void Constructor_WithParent_AddsFolderToParent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var t1 = t0.AddHours(1);

        var root = new Folder(1, "Root", null, t0, t0);
        var child = new Folder(2, "Child", root, t1, t1);

        Assert.Equal(root, child.Parent);
        Assert.Contains(child, root.Children);
    }

    [Fact]
    public void Rename_UpdatesName_AndRaisesEvents()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var t1 = t0.AddMinutes(5);
        var folder = new Folder(1, "Old", null, t0, t0);

        SaveAndClearEvents(folder);

        folder.Rename("New", t1);

        Assert.Equal("New", folder.Name);
        Assert.Contains(folder.DomainEvents, e => e is FolderUpdatedNotification);
        Assert.Contains(folder.DomainEvents, e => e is FolderRenamedNotification);
    }

    [Fact]
    public void Rename_SameName_DoesNotRaiseEvents()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Same", null, t0, t0);

        SaveAndClearEvents(folder);

        folder.Rename("Same", t0.AddMinutes(5));

        Assert.Empty(folder.DomainEvents);
    }

    [Fact]
    public void Move_ChangesParent_AndRaisesMovedEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var t1 = t0.AddMinutes(10);

        var parentA = new Folder(1, "A", null, t0, t0);
        var parentB = new Folder(2, "B", null, t0, t0);
        var child = new Folder(3, "Child", parentA, t0, t0);

        SaveAndClearEvents(parentA, parentB, child);

        child.Move(parentB, t1);

        Assert.Equal(parentB, child.Parent);
        Assert.DoesNotContain(child, parentA.Children);
        Assert.Contains(child, parentB.Children);

        var moved = Assert.Single(child.DomainEvents.OfType<FolderMovedNotification>());
        Assert.Equal(parentA, moved.OldParent);
    }

    [Fact]
    public void Remove_UnparentsFolder_AndRaisesRemovedEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var t1 = t0.AddMinutes(15);

        var parent = new Folder(1, "Parent", null, t0, t0);
        var child = new Folder(2, "Child", parent, t0, t0);

        SaveAndClearEvents(parent, child);

        child.Remove(t1);

        Assert.DoesNotContain(child, parent.Children);
        Assert.Contains(child.DomainEvents, e => e is FolderRemovedNotification);
    }

    [Fact]
    public void GetAllDocuments_ReturnsDocumentsFromHierarchy()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var root = new Folder(1, "Root", null, t0, t0);
        var child = new Folder(2, "Child", root, t0, t0);

        var doc1 = new Document(100, "RootDoc", t0.DateTime, root, true, 1, "pdf", StreamOf("root"), t0.AddMinutes(1), null);
        var doc2 = new Document(200, "ChildDoc", t0.DateTime, child, true, 1, "pdf", StreamOf("child"), t0.AddMinutes(2), null);

        var allDocs = root.GetAllDocuments().ToList();

        Assert.Equal(2, allDocs.Count);
        Assert.Contains(allDocs, d => d.Id == doc1.Id);
        Assert.Contains(allDocs, d => d.Id == doc2.Id);
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
