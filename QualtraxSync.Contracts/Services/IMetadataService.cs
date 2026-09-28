namespace QualtraxSync.Contracts.Services;

/// <summary>
/// A service that applies and retrieves content type metadata to SharePoint documents and folders.  An ISchemaConfiguration<<typeparamref name="T"/>> must be registered for each metadata type that is to be used with this service.
/// </summary>
public interface IMetadataService 
{
    /// <summary>
    /// Applies the the given metadata to item with the specified path in SharePoint, updating the content type if necessary.
    /// </summary>
    Task UpdateAsync<T>(string driveId, string path, T metadata, CancellationToken cancellation = default) where T : class;

    /// <summary>
    /// Gets the field values for the item with the specified path in SharePoint and maps them to a <typeparamref name="T"/>.
    /// </summary>
    Task<T> GetAsync<T>(string driveId, string path, CancellationToken cancellation = default) where T : class;

    /// <summary>
    /// Gets all items of the given metadata type in SharePoint and maps them to a list of <typeparamref name="T"/> keyed by their unique paths.
    /// </summary>
    Task<Dictionary<string,T>> GetAllAsync<T>(string driveId, CancellationToken cancellation = default) where T : class;

    /// <summary>
    /// Ensures that the given metadata type is provisioned in SharePoint.
    /// </summary>
    Task ProvisionAsync(string driveId, Type type, CancellationToken cancellation = default);
}
