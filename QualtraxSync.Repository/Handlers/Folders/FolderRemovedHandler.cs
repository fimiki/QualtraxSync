using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QualtraxSync.Domain.Base;
using QualtraxSync.Domain.Entities;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Persistence.Handlers.Documents;
using QualtraxSync.Persistence.Services;
using QualtraxSync.Persistence.Services.Materializers;

namespace QualtraxSync.Persistence.Handlers.Folders;

/// <summary>
/// Handles the removal of a folder from persistence
/// </summary>
public class FolderRemovedHandler(
    ILogger<FolderRemovedHandler> logger,
    IOptions<Options> options,
    IFileService fileService,
    EntityStore<Folder, int> folders,
    FolderRoots roots,
    PathService pathService) : INotificationHandler<FolderRemovedNotification>
{
    /// <summary>
    /// Depends on all notifications that may result in a folder being moved or removed before applying the removal
    /// </summary>
    public IEnumerable<Type> Dependencies => [typeof(DocumentMovedHandler)];

    public async Task HandleAsync(FolderRemovedNotification notification, CancellationToken cancellationToken = default)
    {
        var folder = notification.Folder;
        if (folder.Dependents())
        {
            logger.LogWarning("Folder {Folder} {Id} has dependents and cannot be removed.", folder.Name, folder.Id);
            return;
        }

        if (folder.Id < -100 && folder.Parent?.Children.FirstOrDefault(c => c.Name == folder.Name) is { } replacement)
        {
            logger.LogDebug("Temporary folder for {Folder} has been assigned Id {Id} and will be removed from tracking.", folder.Name, replacement.Id);
            folders.Remove(folder);
            return;
        }

        foreach (var lifecycle in folder.Lifecycles(options.Value.Lifecycle.UseFolders))
        {
            var path = pathService.Path(folder, false, lifecycle);

            await fileService.DeleteAsync(options.Value.Site.DriveId, path, cancellationToken);

            logger.LogInformation("Removed {Folder} {Id} from {Path}", folder.Name, folder.Id, path);
        }

        folders.Remove(folder);

        if (folder.Parent == null)
        {
            if (options.Value.Lifecycle.UseFolders)
            {
                var path = pathService.Path(folder, false, null);
                await fileService.DeleteAsync(options.Value.Site.DriveId, path, cancellationToken);
            }

            roots.Remove(folder);
        }
        else if (folder.Parent.Children.Any() == false && folder.Parent.Documents.Any() == false)
        {
            folder.Parent.Remove(folder.Modified);
        }
    }
}