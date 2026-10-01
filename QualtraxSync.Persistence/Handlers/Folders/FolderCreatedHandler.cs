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
/// Applies the Qualtrax ID to any newly created folders
/// </summary>
public class FolderCreatedHandler(
    ILogger<FolderCreatedHandler> logger,
    IOptions<Options> options,
    IMetadataService metadataService,
    PathService pathService) : INotificationHandler<FolderCreatedNotification>
{
    public int ConcurrentExecutions => 10;

    /// <summary>
    /// Wait on all handlers for events that may have created folders or added lifecycles
    /// </summary>
    public IEnumerable<Type> Dependencies => [
        typeof(DocumentMovedHandler),
        typeof(RevisionArchivedHandler),
        typeof(RevisionCreatedHandler)];

    public async Task HandleAsync(FolderCreatedNotification notification, CancellationToken cancellationToken = default)
    {
        var folder = notification.Folder;

        if (folder.Id < -100) return; // Ignore temporary folders created during provisioning to track orphaned items

        var metadata = Models.Folder.FromDomainEntity(folder);
        var driveId = options.Value.Site.DriveId;
        var useLifecycles = options.Value.Lifecycle.UseFolders && folder.Root() != folder;

        foreach (var lifecycle in folder.Lifecycles(useLifecycles))
        {
            var path = pathService.Path(folder, false, lifecycle);
            await metadataService.UpdateAsync(driveId, path, metadata, cancellationToken);
            logger.LogInformation("Created {Folder} {Id} at {Path}", folder.Name, folder.Id, path);
        }
    }
}
