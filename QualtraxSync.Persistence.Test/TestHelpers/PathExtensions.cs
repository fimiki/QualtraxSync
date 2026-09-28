using QualtraxSync.Domain.Entities;
using QualtraxSync.Persistence.Services;

namespace QualtraxSync.Persistence.Test.TestHelpers;

public static class PathExtensions
{
    /// <summary>
    /// The path the revision currently occupies in SharePoint (lifecycle folders enabled).
    /// </summary>
    public static string CurrentPath(this Revision revision, PathService pathService) => pathService.Path(revision, true);

    /// <summary>
    /// The path the revision occupied in SharePoint before the pending changes are persisted.
    /// </summary>
    public static string PreviousPath(this Revision revision, PathService pathService) => pathService.Path(revision, false);

    public static string CurrentPath(this Folder folder, Lifecycle lifecycle, PathService pathService) => pathService.Path(folder, true, lifecycle);

    public static string PreviousPath(this Folder folder, Lifecycle lifecycle, PathService pathService) => pathService.Path(folder, false, lifecycle);
}
