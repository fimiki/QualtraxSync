using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QualtraxSync.Contracts.Models;
using QualtraxSync.Qualtrax.Client;
using QualtraxSync.Qualtrax.Models;
using QualtraxSync.Qualtrax.Service;
using QualtraxSync.Qualtrax.Test.TestHelpers;

namespace QualtraxSync.Qualtrax.Test.Services;

public class QualtraxItemServiceTests
{
    private static QualtraxItemService CreateService(Mock<IApiClient> api) =>
        new(NullLogger<QualtraxItemService>.Instance, api.Object, TimeProvider.System);

    // ---- ToDocumentAsync ----

    [Fact]
    public async Task ToDocumentAsync_WithFolderParent_ReturnsDocument_WithFolderAssigned()
    {
        var api = new Mock<IApiClient>();
        var folder = ModelFactory.CreateFolder(id: 50, title: "Procedures", parentId: 0);
        api.Setup(a => a.GetFolderAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(folder);

        var service = CreateService(api);
        var document = ModelFactory.CreateDocument(1, "SOP-1", "Published", 1, parentId: 50, parentType: "Folder", created: DateTimeOffset.UtcNow);

        var result = await service.ToDocumentAsync(document, CancellationToken.None);

        Assert.Equal(1, result.Id);
        Assert.Equal("SOP-1", result.Name);
        Assert.Equal(QualtraxStatus.Published, result.Status);
        Assert.Equal(50, result.Folder?.Id);
        Assert.Equal("Procedures", result.Folder?.Name);
    }

    [Fact]
    public async Task ToDocumentAsync_WithDocumentParent_ReturnsDocument_ResolvedThroughParentDocument()
    {
        var api = new Mock<IApiClient>();
        var parentFolder = ModelFactory.CreateFolder(id: 60, title: "Root", parentId: 0);
        var parentDocument = ModelFactory.CreateDocument(2, "Binder", "Published", 1, parentId: 60, parentType: "Folder", created: DateTimeOffset.UtcNow);

        api.Setup(a => a.GetDocumentAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(parentDocument);
        api.Setup(a => a.GetFolderAsync(60, It.IsAny<CancellationToken>())).ReturnsAsync(parentFolder);

        var service = CreateService(api);
        var document = ModelFactory.CreateDocument(3, "Child Doc", "Published", 1, parentId: 2, parentType: "Document", created: DateTimeOffset.UtcNow);

        var result = await service.ToDocumentAsync(document, CancellationToken.None);

        Assert.Equal(2, result.Folder?.Id);
        Assert.Equal("Binder", result.Folder?.Name);
    }

    [Fact]
    public async Task ToDocumentAsync_MapsUnknownStatus_ToOther()
    {
        var api = new Mock<IApiClient>();
        var folder = ModelFactory.CreateFolder(id: 50, title: "Procedures", parentId: 0);
        api.Setup(a => a.GetFolderAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(folder);

        var service = CreateService(api);
        var document = ModelFactory.CreateDocument(1, "SOP-1", "InReview", 1, parentId: 50, parentType: "Folder", created: DateTimeOffset.UtcNow);

        var result = await service.ToDocumentAsync(document, CancellationToken.None);

        Assert.Equal(QualtraxStatus.Other, result.Status);
    }

    [Fact]
    public async Task ToDocumentAsync_CachesFolderLookup_AcrossMultipleCalls()
    {
        var api = new Mock<IApiClient>();
        var folder = ModelFactory.CreateFolder(id: 70, title: "Cached", parentId: 0);
        api.Setup(a => a.GetFolderAsync(70, It.IsAny<CancellationToken>())).ReturnsAsync(folder);

        var service = CreateService(api);
        var documentA = ModelFactory.CreateDocument(1, "A", "Published", 1, parentId: 70, parentType: "Folder", created: DateTimeOffset.UtcNow);
        var documentB = ModelFactory.CreateDocument(2, "B", "Published", 1, parentId: 70, parentType: "Folder", created: DateTimeOffset.UtcNow);

        await service.ToDocumentAsync(documentA, CancellationToken.None);
        await service.ToDocumentAsync(documentB, CancellationToken.None);

        api.Verify(a => a.GetFolderAsync(70, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToDocumentAsync_ResolvesNestedFolderHierarchy()
    {
        var api = new Mock<IApiClient>();
        var child = ModelFactory.CreateFolder(id: 80, title: "Child", parentId: 81);
        var parent = ModelFactory.CreateFolder(id: 81, title: "Parent", parentId: 0);

        api.Setup(a => a.GetFolderAsync(80, It.IsAny<CancellationToken>())).ReturnsAsync(child);
        api.Setup(a => a.GetFolderAsync(81, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var service = CreateService(api);
        var document = ModelFactory.CreateDocument(1, "Doc", "Published", 1, parentId: 80, parentType: "Folder", created: DateTimeOffset.UtcNow);

        var result = await service.ToDocumentAsync(document, CancellationToken.None);

        Assert.Equal(80, result.Folder?.Id);
        Assert.Equal(81, result.Folder?.Folder?.Id);
        Assert.Null(result.Folder?.Folder?.Folder);
    }

    // ---- ToRevisionAsync : published/archived inference ----

    [Fact]
    public async Task ToRevisionAsync_CurrentRevision_UsesFileLastWriteTimeAsPublished_AndLeavesArchivedNull()
    {
        using var scope = new TempFileScope();

        var after = new DateTime(2026, 1, 1);
        var before = after.AddDays(2);
        var lastWrite = after.AddHours(5);

        var file = scope.CreateFile(documentId: 100, revisionId: 1, title: "SOP");
        File.SetLastWriteTime(file.FullName, lastWrite);

        var api = new Mock<IApiClient>();
        var document = ModelFactory.CreateDocument(100, "SOP", "Published", revision: 1, parentId: 50, parentType: "Folder", created: after.AddDays(-10));
        var folder = ModelFactory.CreateFolder(50, "Root", 0);

        api.Setup(a => a.GetDocumentAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        api.Setup(a => a.GetFolderAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(folder);

        var service = CreateService(api);

        var revision = await service.ToRevisionAsync(file, after, before, CancellationToken.None);

        Assert.Equal(1, revision.Id);
        Assert.Equal(lastWrite, revision.Published);
        Assert.Null(revision.Archived);
    }

    [Fact]
    public async Task ToRevisionAsync_SupersededRevision_IsArchived_AndPublishedUsesWindowOrDocumentCreation()
    {
        using var scope = new TempFileScope();

        var after = new DateTime(2026, 1, 1);
        var before = after.AddDays(2);
        var created = after.AddDays(1);
        var lastWrite = after.AddHours(3);

        // Revision 1 of a document whose latest revision is 2 -> revision 1 has been superseded/archived.
        var file = scope.CreateFile(documentId: 200, revisionId: 1, title: "SOP");
        File.SetLastWriteTime(file.FullName, lastWrite);

        var api = new Mock<IApiClient>();
        var document = ModelFactory.CreateDocument(200, "SOP", "Published", revision: 2, parentId: 50, parentType: "Folder", created: created);
        var folder = ModelFactory.CreateFolder(50, "Root", 0);

        api.Setup(a => a.GetDocumentAsync(200, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        api.Setup(a => a.GetFolderAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(folder);

        var service = CreateService(api);

        var revision = await service.ToRevisionAsync(file, after, before, CancellationToken.None);

        Assert.Equal(lastWrite, revision.Archived);
        Assert.Equal(created, revision.Published); // parent.Created > after, so it wins the Max()
    }

    [Fact]
    public async Task ToRevisionAsync_RetiredDocumentNoLongerAccessible_AssignsSyntheticRetiredParent()
    {
        using var scope = new TempFileScope();

        var after = new DateTime(2026, 1, 1);
        var before = after.AddDays(2);
        var creationTime = after.AddDays(-30); // older than after.AddDays(1), so window start should win
        var lastWrite = after.AddHours(6);

        var file = scope.CreateFile(documentId: 300, revisionId: 1, title: "Old SOP", retired: true);
        File.SetCreationTime(file.FullName, creationTime);
        File.SetLastWriteTime(file.FullName, lastWrite);

        var api = new Mock<IApiClient>();
        api.Setup(a => a.GetDocumentAsync(300, It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);

        var service = CreateService(api);

        var revision = await service.ToRevisionAsync(file, after, before, CancellationToken.None);

        Assert.Equal(QualtraxStatus.Retired, revision.Document.Status);
        Assert.Equal(QualtraxItemService.RetiredFolder.Id, revision.Document.Folder?.Id);
        Assert.Equal(after.AddDays(1), revision.Published);
        Assert.Equal(lastWrite, revision.Archived);
    }

    [Fact]
    public async Task ToRevisionAsync_RetiredRevision_IsCachedAndOnlyResolvedOnce()
    {
        using var scope1 = new TempFileScope();
        using var scope2 = new TempFileScope();

        var after = new DateTime(2026, 1, 1);
        var before = after.AddDays(2);

        var firstFile = scope1.CreateFile(documentId: 400, revisionId: 1, title: "Retired Doc", retired: true, contents: "first");
        var secondFile = scope2.CreateFile(documentId: 400, revisionId: 1, title: "Retired Doc", retired: true, contents: "second");

        var api = new Mock<IApiClient>();
        api.Setup(a => a.GetDocumentAsync(400, It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);

        var service = CreateService(api);

        var firstRevision = await service.ToRevisionAsync(firstFile, after, before, CancellationToken.None);
        var secondRevision = await service.ToRevisionAsync(secondFile, after, before, CancellationToken.None);

        api.Verify(a => a.GetDocumentAsync(400, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(firstRevision.Published, secondRevision.Published);
        Assert.Equal(firstRevision.Archived, secondRevision.Archived);
        Assert.False(File.Exists(firstFile.FullName));
        Assert.False(File.Exists(secondFile.FullName));
    }

    [Fact]
    public async Task ToRevisionAsync_NonRetiredRevisionsWithSameKey_AreNotCached()
    {
        using var scope1 = new TempFileScope();
        using var scope2 = new TempFileScope();

        var after = new DateTime(2026, 1, 1);
        var before = after.AddDays(2);

        var firstFile = scope1.CreateFile(documentId: 500, revisionId: 1, title: "Active Doc");
        var secondFile = scope2.CreateFile(documentId: 500, revisionId: 1, title: "Active Doc");

        var api = new Mock<IApiClient>();
        var document = ModelFactory.CreateDocument(500, "Active Doc", "Published", revision: 1, parentId: 50, parentType: "Folder", created: after.AddDays(-1));
        var folder = ModelFactory.CreateFolder(50, "Root", 0);

        api.Setup(a => a.GetDocumentAsync(500, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        api.Setup(a => a.GetFolderAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(folder);

        var service = CreateService(api);

        await service.ToRevisionAsync(firstFile, after, before, CancellationToken.None);
        await service.ToRevisionAsync(secondFile, after, before, CancellationToken.None);

        api.Verify(a => a.GetDocumentAsync(500, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ---- Document identity between ToDocumentAsync and ToRevisionAsync ----

    [Fact]
    public async Task ToRevisionAsync_DocumentRecord_IsIdenticalTo_ToDocumentAsyncResult()
    {
        using var scope = new TempFileScope();

        var after = new DateTime(2026, 1, 1);
        var before = after.AddDays(2);

        var file = scope.CreateFile(documentId: 600, revisionId: 1, title: "Matching Doc");
        File.SetLastWriteTime(file.FullName, after.AddHours(1));

        var api = new Mock<IApiClient>();
        var document = ModelFactory.CreateDocument(600, "Matching Doc", "Published", revision: 1, parentId: 50, parentType: "Folder", created: after.AddDays(-1));
        var folder = ModelFactory.CreateFolder(50, "Root", 0);

        api.Setup(a => a.GetDocumentAsync(600, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        api.Setup(a => a.GetFolderAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(folder);

        var service = CreateService(api);

        var expectedDocument = await service.ToDocumentAsync(document, CancellationToken.None);
        var revision = await service.ToRevisionAsync(file, after, before, CancellationToken.None);

        Assert.Equal(expectedDocument, revision.Document);
    }
}
