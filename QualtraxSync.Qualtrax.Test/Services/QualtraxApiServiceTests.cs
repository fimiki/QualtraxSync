using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QualtraxSync.Contracts.Models;
using QualtraxSync.Qualtrax.Client;
using QualtraxSync.Qualtrax.Models;
using QualtraxSync.Qualtrax.Service;
using QualtraxSync.Qualtrax.Services;
using QualtraxSync.Qualtrax.Test.TestHelpers;

namespace QualtraxSync.Qualtrax.Test.Services;

public class QualtraxApiServiceTests
{
    [Fact]
    public async Task GetQualtraxDocumentAsync_ReturnsNull_WhenApiReturnsNull()
    {
        var api = new Mock<IApiClient>();
        var itemService = new Mock<IQualtraxItemService>();

        api.Setup(a => a.GetDocumentAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);

        var service = new QualtraxApiService(NullLogger<QualtraxApiService>.Instance, api.Object, itemService.Object);

        var result = await service.GetQualtraxDocumentAsync(1, CancellationToken.None);

        Assert.Null(result);
        itemService.Verify(s => s.ToDocumentAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetQualtraxDocumentAsync_ReturnsConvertedDocument_WhenFound()
    {
        var api = new Mock<IApiClient>();
        var itemService = new Mock<IQualtraxItemService>();

        var document = ModelFactory.CreateDocument(1, "SOP", "Published", 1, parentId: 50, parentType: "Folder", created: DateTimeOffset.UtcNow);
        var folderItem = new QualtraxItem(50, "Root", null);
        var expected = new QualtraxDocument(1, 1, "SOP", folderItem, QualtraxStatus.Published, document.DateCreated);

        api.Setup(a => a.GetDocumentAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        itemService.Setup(s => s.ToDocumentAsync(document, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var service = new QualtraxApiService(NullLogger<QualtraxApiService>.Instance, api.Object, itemService.Object);

        var result = await service.GetQualtraxDocumentAsync(1, CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetQualtraxRevisionsAsync_YieldsOneRevisionPerFile_UsingRootFolder_WhenNoParentGiven()
    {
        var api = new Mock<IApiClient>();
        var itemService = new Mock<IQualtraxItemService>();

        var after = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var before = after.AddDays(1);

        using var tempScope = new TempFileScope();
        var file1 = tempScope.CreateFile(1, 1);
        var file2 = tempScope.CreateFile(2, 1);

        api.Setup(a => a.GetFilesAsync(
                QualtraxItemService.RootFolder.Id,
                after.LocalDateTime,
                before.LocalDateTime,
                It.IsAny<Lazy<DirectoryInfo>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([file1, file2]);

        var revision1 = MakeRevision(1);
        var revision2 = MakeRevision(2);

        itemService.Setup(s => s.ToRevisionAsync(file1, after.LocalDateTime, before.LocalDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(revision1);
        itemService.Setup(s => s.ToRevisionAsync(file2, after.LocalDateTime, before.LocalDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(revision2);

        var service = new QualtraxApiService(NullLogger<QualtraxApiService>.Instance, api.Object, itemService.Object);

        var results = new List<QualtraxRevision>();
        await foreach (var revision in service.GetQualtraxRevisionsAsync(after, before, cancellationToken: CancellationToken.None))
        {
            results.Add(revision);
        }

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Id == 1);
        Assert.Contains(results, r => r.Id == 2);
    }

    [Fact]
    public async Task GetQualtraxRevisionsAsync_UsesParentFolderId_WhenParentGiven()
    {
        var api = new Mock<IApiClient>();
        var itemService = new Mock<IQualtraxItemService>();

        var parent = new QualtraxItem(999, "Some Folder", null);
        var after = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var before = after.AddDays(1);

        api.Setup(a => a.GetFilesAsync(999, after.LocalDateTime, before.LocalDateTime, It.IsAny<Lazy<DirectoryInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = new QualtraxApiService(NullLogger<QualtraxApiService>.Instance, api.Object, itemService.Object);

        var results = new List<QualtraxRevision>();
        await foreach (var revision in service.GetQualtraxRevisionsAsync(after, before, parent, CancellationToken.None))
        {
            results.Add(revision);
        }

        Assert.Empty(results);
        api.Verify(a => a.GetFilesAsync(999, after.LocalDateTime, before.LocalDateTime, It.IsAny<Lazy<DirectoryInfo>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetQualtraxRevisionsAsync_StopsEnumerating_WhenCancellationRequested()
    {
        var api = new Mock<IApiClient>();
        var itemService = new Mock<IQualtraxItemService>();

        using var tempScope = new TempFileScope();
        var file1 = tempScope.CreateFile(1, 1);
        var file2 = tempScope.CreateFile(2, 1);

        var after = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var before = after.AddDays(1);

        api.Setup(a => a.GetFilesAsync(
                QualtraxItemService.RootFolder.Id,
                after.LocalDateTime,
                before.LocalDateTime,
                It.IsAny<Lazy<DirectoryInfo>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([file1, file2]);

        itemService.Setup(s => s.ToRevisionAsync(file1, after.LocalDateTime, before.LocalDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(MakeRevision(1));
        itemService.Setup(s => s.ToRevisionAsync(file2, after.LocalDateTime, before.LocalDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(MakeRevision(2));

        var service = new QualtraxApiService(NullLogger<QualtraxApiService>.Instance, api.Object, itemService.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var results = new List<QualtraxRevision>();
        await foreach (var revision in service.GetQualtraxRevisionsAsync(after, before, cancellationToken: cts.Token))
        {
            results.Add(revision);
        }

        Assert.Empty(results);
        itemService.Verify(s => s.ToRevisionAsync(It.IsAny<FileInfo>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static QualtraxRevision MakeRevision(int id)
    {
        var folder = new QualtraxItem(50, "Root", null);
        var document = new QualtraxDocument(id, id, $"Doc{id}", folder, QualtraxStatus.Published, DateTimeOffset.UtcNow);

        return new QualtraxRevision(id, document, ".pdf", new MemoryStream(), DateTimeOffset.UtcNow, null);
    }
}
