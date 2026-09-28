using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Models;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Domain.Repositories;
using System.Diagnostics;

namespace QualtraxSync.Services.Services;

public interface IDocumentService
{
    Task RefreshDocumentAsync(Document document, CancellationToken cancellationToken = default);
    Task<bool> SyncRevisionAsync(QualtraxRevision revision, CancellationToken cancellationToken);
}

public class DocumentService(
    IDocumentRepository documents,
    IQualtraxApiService apiService,
    IFolderService folderService,
    IOptions<Options> options,
    ILogger<DocumentService> logger,
    TimeProvider time) : IDocumentService
{
    private readonly static SemaphoreSlim _semaphore = new(1);
    private readonly Options _options = options.Value;

    public async Task RefreshDocumentAsync(Document document, CancellationToken cancellationToken = default) => await RefreshDocumentAsync(document, null, cancellationToken);

    public async Task<bool> SyncRevisionAsync(QualtraxRevision revision, CancellationToken cancellationToken)
    {
        var qualtraxFolder = revision.Document.Folder ?? new QualtraxItem(-1, "Root", null);
        var latest = revision.Id == revision.Document.LatestRevision;

        var document = await documents.GetAsync(revision.Document.Id, cancellationToken);
        if (document != null)
        {
            // first, check if this revision already exists and if it has changed
            // important that this goes first as the api can return the same revision repeatedly for different time periods
            var existing = document.Revisions.FirstOrDefault(r => r.Id.Revision == revision.Id);
            if (existing != null)
            {
                if (existing.Archived.HasValue == revision.Archived.HasValue) return false;

                if (revision.Archived.HasValue)
                {
                    document.Retire(revision.Archived.Value);
                }
                else
                {
                    document.Unretire(time.GetUtcNow());
                }
            }

            // then, check if the name or location needs to be updated or if any previous revisions are missing
            await RefreshDocumentAsync(document, revision.Document, cancellationToken);

            // finally, add the revision to this existing document
            if (existing == null)
            {
                document.Revise(revision.Id, latest, revision.Extension, revision.Contents, revision.Published, revision.Archived);
            }

            return true;
        }

        if (_options.SyncAllRevisions && revision.Id > 1)
        {
            // revisions should be added to a document sequentially, so if this is not the first revision we need to check if any previous revisions are missing and add them first
            return await SyncPreviousRevisionsAsync(revision, cancellationToken);
        }

        // add the new document to the repository
        // important to keep this single-threaded since it involves potential folder creation which could result in race conditions if multiple documents in the same folder are being added
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            var folder = await folderService.GetUpdatedAsync(qualtraxFolder, revision.Archived ?? revision.Published, cancellationToken);

            document = new(revision.Document.Id, revision.Document.Name, revision.Document.Created, folder, latest, revision.Id, revision.Extension, revision.Contents, revision.Published, revision.Archived);
            documents.Add(document);
        }
        finally
        {
            _semaphore.Release();
        }

        return true;
    }

    private async Task RefreshDocumentAsync(Document document, QualtraxDocument? updated, CancellationToken cancellationToken = default)
    {
        if (document.Retired)
        {
            // if the document is retired it can't be renamed or moved, only unretired which will be triggered through a revision update
            return;
        }

        updated ??= await apiService.GetQualtraxDocumentAsync(document.Id, cancellationToken);

        var now = time.GetUtcNow();

        // check if the API has lost access to this document and needs to be removed from the repository.
        if (updated == null)
        {
            document.Remove(now);
            return;
        }

        // check if the document has been renamed
        if (document.Name != updated.Name)
        {
            document.Rename(updated.Name, now);
        }

        // check the document has been moved to a different folder
        // keep this single-threaded to avoid race conditions on folder creation/update
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            var parent = updated.Folder ?? new QualtraxItem(-1, "Root", null);
            var folder = await folderService.GetUpdatedAsync(parent, now, cancellationToken);

            if (folder.Id != document.Folder.Id && folder != Folder.Retired && folder != Folder.Unretired)
            {
                document.Move(folder, now);
            }
        }
        finally
        {
            _semaphore.Release();
        }

        // check if any previous revisions are missing
        if (_options.SyncAllRevisions)
        {
            await SyncMissingRevisionsAsync(document, cancellationToken);
        }
    }

    private async Task<bool> SyncPreviousRevisionsAsync(QualtraxRevision revision, CancellationToken cancellationToken = default)
    {
        var id = 1;
        var start = revision.Document.Created;
       
        while (id < revision.Id)
        {
            if (await FindRevisionAsync(id, revision.Document.Id, revision.Document.Folder, start, revision.Published, cancellationToken) is { } previous)
            {
                await SyncRevisionAsync(previous, cancellationToken);
            }
            else
            {
                throw new Exception($"Could not find found revision {id} for document {revision.Document.Id} {revision.Document.Name}");
            }

            id++;
            start = previous.Archived ?? previous.Published;
        }

        return await SyncRevisionAsync(revision, cancellationToken);
    }

    private async Task SyncMissingRevisionsAsync(Document document, CancellationToken cancellationToken = default)
    {
        var latest = document.Revisions.OrderByDescending(r => r.Id.Revision).First();
        var id = 1;

        while (id < latest.Id.Revision)
        {
            if (document.Revisions.FirstOrDefault(r => r.Id.Revision == id) is null)
            {
                var previous = document.Revisions.OrderByDescending(r => r.Id.Revision).FirstOrDefault(r => r.Id.Revision < id);
                var start = (previous == null ? document.Created : previous.Archived ?? previous.Published).AddMinutes(-1);
                var end = (previous == null ? latest.Published : start.Date).AddMinutes(1);

                if (await FindRevisionAsync(id, document.Id, folderService.Map(document.Folder), start, end, cancellationToken) is { } found)
                {
                    document.Revise(found.Id, false, found.Extension, found.Contents, found.Published, found.Archived);
                }
                else
                {
                    throw new Exception($"Could not find found revision {id} for document {document.Id} {document.Name}");
                }
            }

            id++;
        }
    }

    private async Task<QualtraxRevision?> FindRevisionAsync(int revisionId, int documentId, QualtraxItem? parent, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var timestamp = time.GetTimestamp();
        var after = start;
        var before = end - start < TimeSpan.FromDays(1) ? end : start.Date.AddDays(1);

        while (before <= end)
        {
            try
            {
                await foreach (var found in apiService.GetQualtraxRevisionsAsync(after, before, parent, cancellationToken))
                {
                    if (found.Document.Id == documentId && found.Id == revisionId)
                    {
                        return found;
                    }
                    else
                    {
                        found.Contents.Dispose();
                    }
                }

                after = before;
                before = after.AddDays(1);
            }
            catch (TimeoutException) when (before - after > TimeSpan.FromSeconds(1))
            {
                logger.LogWarning("File retrieval for folder {Id} {Name} between {After} and {Before} timed out after {Time} seconds.  Splitting request due to presumed excessive response size", parent?.Id ?? -1, parent?.Name ?? "Root", after, before, time.GetElapsedTime(timestamp).TotalSeconds);

                var splitCount = 12;
                var splitPeriod = (before - after) / splitCount;

                for (int i = 0; i < splitCount; i++)
                {
                    var rangeStart = after + splitPeriod * i;
                    var rangeEnd = after + splitPeriod * (i + 1);
                    if (rangeEnd > before)
                    {
                        rangeEnd = before;
                    }

                    if (await FindRevisionAsync(revisionId, documentId, parent, rangeStart, rangeEnd, cancellationToken) is { } found)
                    {
                        return found;
                    }

                    start = rangeEnd;
                }

                return await FindRevisionAsync(revisionId, documentId, parent, start, end, cancellationToken);
            }
        }

        return null;
    }

}

