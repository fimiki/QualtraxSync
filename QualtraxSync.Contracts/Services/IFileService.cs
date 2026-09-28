namespace QualtraxSync.Contracts.Services;

public interface IFileService
{
    /// <summary>
    /// Creates a new folder at the specified path in SharePoint.  If the folder already exists, this method will return true.
    /// </summary>
    Task<bool> CreateAsync(string driveId, string path, DateTimeOffset? created = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the file or folder at the specified path in SharePoint.  If the item does not exist, this method will return true.
    /// </summary>
    Task<bool> DeleteAsync(string driveId, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads the given contents to the specified path in SharePoint.  If the file already exists, it will be overwritten.
    /// </summary>
    Task<bool> UploadAsync(string driveId, string path, Stream contents, DateTimeOffset? created = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves and/or renames the file or folder at the specified old path to the new path in SharePoint.  If the item does not exist, this method will return false.
    /// </summary>
    Task<bool> MoveAsync(string driveId, string oldPath, string newPath, CancellationToken cancellationToken = default);
}