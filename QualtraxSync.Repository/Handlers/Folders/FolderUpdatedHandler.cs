using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Handlers.Documents;
using QualtraxSync.Persistence.Handlers.Revisions;
using QualtraxSync.Persistence.Services;

namespace QualtraxSync.Persistence.Handlers.Folders;

/// <summary>
/// Refreshes the metadata for a folder whose contents have been updated and trims any empty lifecycle folders that may have resulted from moves or deletions
/// </summary>
public class FolderUpdatedHandler(
    ILogger<FolderUpdatedHandler> logger,
    IOptions<Options> options,
    IMetadataService metadataService,
    IFileService fileService,
    PathService pathService) : INotificationHandler<FolderUpdatedNotification>
{
    public int ConcurrentExecutions => 20;

    /// <summary>
    /// Depends on all notifications being processed before applying a refresh
    /// </summary>
    public IEnumerable<Type> Dependencies => [
        typeof(FolderMovedHandler),
        typeof(FolderCreatedHandler),
        typeof(FolderRemovedHandler),
        typeof(DocumentMovedHandler),
        typeof(DocumentRemovedHandler),
        typeof(RevisionCreatedHandler),
        typeof(RevisionArchivedHandler)];

    public async Task HandleAsync(FolderUpdatedNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.Folder.DomainEvents.OfType<FolderRemovedNotification>().Any()) return;

        var folder = notification.Folder;
        var oldLifecycles = notification.OldLifecycles;

        foreach (var lifecycle in folder.Lifecycles(options.Value.Lifecycle.UseFolders))
        {
            oldLifecycles.Remove(lifecycle);

            var path = pathService.Path(folder, true, lifecycle);
            await metadataService.UpdateAsync(options.Value.Site.DriveId, path, Models.Folder.FromDomainEntity(folder), cancellationToken);
            logger.LogInformation("Refreshed timestamp for {Folder} {Id} at {Path}", folder.Name, folder.Id, path);
        }

        foreach (var lifecycle in oldLifecycles)
        {
            if (lifecycle is null) continue;

            var path = pathService.Path(folder, true, lifecycle);
            await fileService.DeleteAsync(options.Value.Site.DriveId, path, cancellationToken);
            logger.LogInformation("Removed empty lifecycle folder for {Folder} {Id} at {Path}", folder.Name, folder.Id, path);
        }
    }
}
