using System.Text;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using Xunit;

namespace QualtraxSync.Domain.Test.Entities;

public class RevisionTests
{
    [Fact]
    public void Retire_ArchivesRevision_AndRaisesArchivedEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "Doc", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), null);
        var revision = Assert.Single(document.Revisions);

        SaveAndClearEvents(document, revision);

        document.Retire(t0.AddMinutes(2));

        Assert.True(document.Retired);
        Assert.NotNull(revision.Archived);
        Assert.Contains(revision.DomainEvents, e => e is RevisionArchivedNotification);
    }

    [Fact]
    public void Retire_AlreadyArchivedRevision_DoesNotRaiseDuplicateEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "Doc", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), t0.AddMinutes(2));
        var revision = Assert.Single(document.Revisions);

        document.Retire(t0.AddMinutes(3));

        Assert.DoesNotContain(revision.DomainEvents, e => e is RevisionArchivedNotification);
    }

    [Fact]
    public void Unretire_ReactivatesLatestRevision_AndRaisesReactivatedEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "Doc", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), null);
        var revision = Assert.Single(document.Revisions);

        document.Retire(t0.AddMinutes(2));

        SaveAndClearEvents(document, revision);

        document.Unretire(t0.AddMinutes(3));

        Assert.False(document.Retired);
        Assert.Null(revision.Archived);
        Assert.Contains(revision.DomainEvents, e => e is RevisionReactivatedNotification);
    }

    [Fact]
    public void Unretire_WhenNotArchived_DoesNotRaiseEvent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "Doc", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), null);
        var revision = Assert.Single(document.Revisions); 
        
        SaveAndClearEvents(document, revision);

        document.Unretire(t0.AddMinutes(2));

        Assert.DoesNotContain(revision.DomainEvents, e => e is RevisionReactivatedNotification);
    }

    [Fact]
    public void Name_FormatsExpectedRevisionName()
    {
        var t0 = Local(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "SOP", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddDays(1), null);
        var revision = Assert.Single(document.Revisions);

        var name = revision.Name();

        Assert.Equal("SOP - 001 - 2026-01-02.pdf", name);
    }

    [Fact]
    public void Name_WhenArchived_IncludesThroughDate()
    {
        var t0 = Local(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "SOP", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddDays(1), t0.AddDays(5));
        var revision = Assert.Single(document.Revisions);

        Assert.Equal("SOP - 001 - 2026-01-02 through 2026-01-06.pdf", revision.Name());
    }

    [Fact]
    public void Lifecycle_ForCurrentRevision_ReturnsCurrent()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "Doc", t0.DateTime, folder, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), null);
        var revision = Assert.Single(document.Revisions);

        Assert.Equal(Lifecycle.Current, revision.Lifecycle());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Lifecycle_ForArchivedRevision_ShouldReturnRetiredWhenLatestAndArchivedWhenNot(bool latest)
    {
        var t0 = Utc(2026, 1, 1, 0);
        var folder = new Folder(1, "Root", null, t0, t0);
        var document = new Document(10, "Doc", t0.DateTime, folder, latest, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), t0.AddMinutes(2));
        var revision = Assert.Single(document.Revisions);

        Assert.Equal(latest ? Lifecycle.Retired : Lifecycle.Archived, revision.Lifecycle());
    }

    [Fact]
    public void Lifecycle_ForRetiredDocument_ReturnsRetired()
    {
        var t0 = Utc(2026, 1, 1, 0);
        var retired = Folder.Retired;
        var document = new Document(10, "Doc", t0.DateTime, retired, true, 1, "pdf", StreamOf("rev1"), t0.AddMinutes(1), t0.AddMinutes(2));
        var revision = document.Revisions.Single();

        Assert.Equal(Lifecycle.Retired, revision.Lifecycle());
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

    private static DateTimeOffset Local(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.FromHours(-5));

    private static Stream StreamOf(string text) =>
        new MemoryStream(Encoding.UTF8.GetBytes(text));
}
