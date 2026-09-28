using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Models;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using System.Diagnostics;

namespace QualtraxSync.Services.Services;

public interface ISyncService
{
    Task ProcessAsync(CancellationToken cancellationToken);
}

public class SyncService(
    ILogger<SyncService> logger,
    IOptions<Options> options,
    IQualtraxApiService api,
    IFolderService folderService,
    IDocumentService documentService,
    IUnitOfWork unitOfWork,
    TimeProvider time) : ISyncService
{
    private static DateTimeOffset _lastChange = default;
    private static DateTimeOffset _lastRefresh = default;

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        await SyncAsync(cancellationToken);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var now = time.GetLocalNow();

        // run once per day on the first execution after midnight
        if (_lastRefresh.Date >= now.Date) return;

        logger.LogDebug("Running daily document name and location refresh for {Date}.", now.Date);

        unitOfWork.ResetContext();

        var refreshes = new List<Task>();
        var roots = await folderService.GetRootsAsync(cancellationToken);

        foreach (var folder in roots.ToList())
        {
            var documents = folder.GetAllDocuments().ToList();

            logger.LogDebug("Checking {Count} documents in root folder {Name} {Id}", documents.Count, folder.Id, folder.Name);

            foreach (var document in documents)
            {
                var max = document.Revisions.Max(r => r.Published).Date;
                if (max > _lastChange)
                {
                    _lastChange = max;
                }

                refreshes.Add(documentService.RefreshDocumentAsync(document, cancellationToken));

                while (refreshes.Count > 20)
                {
                    var finished = await Task.WhenAny(refreshes);
                    refreshes.Remove(finished);
                }
            }

            await Task.WhenAll(refreshes);
            refreshes.Clear();
        }

        logger.LogDebug("Daily document name and location refresh completed in {Time} seconds.  Saving changes...", (time.GetLocalNow() - now).TotalSeconds);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        _lastRefresh = now;
    }

    private Task SyncAsync(CancellationToken cancellationToken) => SyncRecordsAsync(new SyncParameters(_lastChange, null, null, 0, cancellationToken));

    private async Task SyncRecordsAsync(SyncParameters parameters)
    {
        var end = parameters.PublishedBefore ?? time.GetLocalNow();
        var initializing = true; // _lastChange == options.Value.EarliestDocument;
        var stopwatch = Stopwatch.StartNew();

        Task? savingLastChanges = null;

        var publishedAfter = parameters.PublishedAfter;
        var publishedBefore = parameters.PublishedBefore ?? (parameters.PublishedAfter <= options.Value.EarliestDocument ? options.Value.EarliestDocument : parameters.PublishedAfter).Date.AddDays(1);



        var roots = (await folderService.GetRootsAsync(parameters.CancellationToken)).ToHashSet();

        logger.LogInformation("Got root folders in {Seconds}", stopwatch.Elapsed.TotalSeconds);

        do
        {
            try
            {
                var syncedRevisions = new List<QualtraxRevision>();

                stopwatch.Restart();

                logger.LogDebug("Getting records for folder {Id} {Name} between {After} and {Before}", parameters.Parent?.Id ?? -1, parameters.Parent?.Name ?? "Root", publishedAfter, publishedBefore);

                await foreach (var revision in api.GetQualtraxRevisionsAsync(publishedAfter, publishedBefore, folderService.Map(parameters.Parent), parameters.CancellationToken))
                {
                    if (savingLastChanges != null)
                    {
                        // ensure the last changes are saved before continuing to process this period's revisions
                        await savingLastChanges;
                    }

                    if (roots.Select(f => f.Id).Contains(folderService.GetRoot(revision.Document).Id) == false
                        && (initializing == false || time.GetLocalNow() - end > TimeSpan.FromDays(3)))
                    {
                        // if this is a new root folder, the API may have just gained access to it, so we need to sync the entire history of that root folder before continuing
                        var folder = await SyncNewRootAsync(publishedAfter, folderService.GetRoot(revision.Document), parameters.CancellationToken);

                        roots.Add(folder);
                    }

                    if (await documentService.SyncRevisionAsync(revision, parameters.CancellationToken))
                    {
                        syncedRevisions.Add(revision);
                    }
                }

                if (syncedRevisions.Count > 0)
                {
                    // start saving the changes in the background before looping to fetch more revisions
                    var saveParameters = parameters with 
                    { 
                        PublishedAfter = publishedAfter, 
                        PublishedBefore = publishedBefore, 
                        Tries = 0 
                    };

                    savingLastChanges = SaveChangesAsync(saveParameters, syncedRevisions);
                }
            }
            catch (TimeoutException)
            {
                stopwatch.Stop();

                if (publishedBefore - publishedAfter < TimeSpan.FromSeconds(1))
                {
                    logger.LogWarning("File retrieval for folder {Id} {Name} between {After} and {Before} timed out after {Time} seconds.  Splitting request among known Subfolders due to presumed excessive response size", parameters.Parent?.Id ?? -1, parameters.Parent?.Name ?? "Root", publishedAfter, publishedBefore, stopwatch.Elapsed.TotalSeconds);

                    var subFolders = parameters.Parent == null ? roots : [.. parameters.Parent.Children];

                    if (subFolders.Count == 0)
                    {
                        logger.LogWarning("No subfolders found for folder {Id} {Name} between {After} and {Before}.  Cannot split request further", parameters.Parent?.Id ?? -1, parameters.Parent?.Name ?? "Root", publishedAfter, publishedBefore);
                        throw;
                    }

                    foreach (var subFolder in subFolders)
                    {
                        var splitSyncParameters = parameters with 
                        { 
                            PublishedAfter = publishedAfter, 
                            PublishedBefore = publishedBefore, 
                            Parent = subFolder, 
                            Tries = 0 
                        };

                        await SyncRecordsAsync(splitSyncParameters);
                    }
                }

                logger.LogWarning("File retrieval for folder {Id} {Name} between {After} and {Before} timed out after {Time} seconds.  Splitting request due to presumed excessive response size", parameters.Parent?.Id ?? -1, parameters.Parent?.Name ?? "Root", publishedAfter, publishedBefore, stopwatch.Elapsed.TotalSeconds);

                var splitCount = 12;
                var splitPeriod = (publishedBefore - publishedAfter) / splitCount;

                for (int i = 0; i < splitCount; i++)
                {
                    var rangeStart = publishedAfter + splitPeriod * i;
                    var rangeEnd = publishedAfter + splitPeriod * (i + 1);
                    if (rangeEnd > publishedBefore)
                    {
                        rangeEnd = publishedBefore;
                    }

                    var splitSyncParameters = parameters with 
                    { 
                        PublishedAfter = rangeStart, 
                        PublishedBefore = rangeEnd, 
                        Tries = 0 
                    };

                    await SyncRecordsAsync(splitSyncParameters);
                }
            }
            catch (Exception ex) when (parameters.Tries < 3)
            {
                var retryParameters = parameters with 
                { 
                    PublishedAfter = publishedAfter,
                    PublishedBefore = publishedBefore,
                    Tries = parameters.Tries + 1 
                };

                logger.LogWarning(ex, "An error occurred while syncing records for folder {Id} {Name} between {After} and {Before}.  Retrying sync ({Try}/{MaxTries})", retryParameters.Parent?.Id ?? -1, retryParameters.Parent?.Name ?? "Root", retryParameters.PublishedAfter, retryParameters.PublishedBefore, retryParameters.Tries, 3);
                await Task.Delay(TimeSpan.FromSeconds(2*retryParameters.Tries)+TimeSpan.FromMilliseconds(Random.Shared.Next(1000)), time, retryParameters.CancellationToken);
                await SyncRecordsAsync(retryParameters);
            }
            catch
            {
                unitOfWork.ResetContext();
                throw;
            }

            publishedAfter = publishedBefore;
            publishedBefore = publishedAfter.AddDays(1);

        } while (publishedAfter < end);

        if (savingLastChanges != null)
        {
            await savingLastChanges;
        }

        // advance the last change date to when the initial sync call started
        if (_lastChange < end && parameters.PublishedBefore.HasValue == false)
        {
            // ensure that when midnight rolls over we start at the beginning
            if (_lastChange.Date < end.Date)
            {
                _lastChange = end.Date;
            }

            _lastChange = end;
        }
    }

    private async Task<Folder> SyncNewRootAsync(DateTimeOffset publishedBefore, QualtraxItem root, CancellationToken cancellationToken)
    {
        if (root.Folder != null)
        {
            throw new InvalidOperationException($"Document {root.Id} {root.Name} is not a root folder");
        }

        var existing = await folderService.GetRootsAsync(cancellationToken);
        if (existing.FirstOrDefault(f => f.Id == root.Id) is Folder folder)
        {
            logger.LogDebug("Root folder {Id} {Name} already exists, skipping", root.Id, root.Name);
            return folder;
        }

        logger.LogDebug("New root folder '{RootFolder}' found. Getting history for all files in this record prior to {PublishedBefore}", root.Name, publishedBefore);

        var estimatedCreation = options.Value.EarliestDocument;

        // use the latest item created prior to this root folder to estimate the creation date
        foreach (var document in existing.SelectMany(e => e.GetAllDocuments().Where(d => d.Id < root.Id)))
        {
            if (document.Created > estimatedCreation)
            {
                estimatedCreation = document.Created;
            }
        }

        folder = await folderService.GetUpdatedAsync(root, publishedBefore, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var parameters = new SyncParameters(options.Value.EarliestDocument, publishedBefore, folder, 0, cancellationToken);

        await SyncRecordsAsync(parameters);

        return folder;
    }

    private async Task SaveChangesAsync(SyncParameters parameters, IEnumerable<QualtraxRevision> revisions)
    {
        var start = time.GetTimestamp();
        var maxAttempts = 3;
        try
        {
            await unitOfWork.SaveChangesAsync(parameters.CancellationToken);
        }
        catch (Exception ex) when (parameters.Tries < maxAttempts)
        {
            var retryParameters = parameters with { Tries = parameters.Tries + 1 };

            logger.LogError(ex, "An error occurred while saving records for folder {Id} {Name} between {After} and {Before}.  Retrying save ({Try}/{MaxTries})", retryParameters.Parent?.Id ?? -1, retryParameters.Parent?.Name ?? "Root", retryParameters.PublishedAfter, retryParameters.PublishedBefore, retryParameters.Tries, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(2 * retryParameters.Tries) + TimeSpan.FromMilliseconds(Random.Shared.Next(1000)), time, retryParameters.CancellationToken);
            await SaveChangesAsync(retryParameters, revisions);
            return;
        }

        var count = 0;
        var lastPublished = DateTimeOffset.MinValue;

        foreach (var revision in revisions)
        {
            // now that the changes have been saved, we can dispose of the contents to free up memory
            revision.Contents.Dispose();

            if (revision.Published > lastPublished)
            {
                lastPublished = revision.Published;
            }

            count++;
        }

        if (lastPublished > _lastChange)
        {
            _lastChange = lastPublished;
        }

        logger.LogInformation("Synced {Count} changes in {Seconds} seconds for records published through {Date} in {Folder} after {Tries} attempts.", count, time.GetElapsedTime(start).TotalSeconds, _lastChange, parameters.Parent?.Name ?? "Root", parameters.Tries + 1);
    }

    private record SyncParameters(DateTimeOffset PublishedAfter, DateTimeOffset? PublishedBefore, Folder? Parent, int Tries, CancellationToken CancellationToken);
}
