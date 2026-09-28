using QualtraxSync.Contracts.Services;

namespace QualtraxSync.Persistence.Test.TestHelpers;

/// <summary>
/// An in-memory stand-in for the SharePoint metadata service.  Seeded content represents items that already exist in
/// SharePoint (and is returned from the GetAll methods), while every UpdateAsync call is recorded so that tests can
/// verify the paths and DTOs that were persisted.
/// </summary>
public sealed class FakeMetadataService : IMetadataService
{
    private readonly Dictionary<string, Models.File> _files = [];
    private readonly Dictionary<string, Models.Folder> _folders = [];
    private readonly List<(string DriveId, string Path, object Metadata)> _updates = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<(string DriveId, string Path, object Metadata)> Updates
    {
        get
        {
            lock (_lock)
            {
                return [.. _updates];
            }
        }
    }

    public IReadOnlyCollection<string> SeededFilePaths => [.. _files.Keys];

    public IReadOnlyCollection<string> SeededFolderPaths => [.. _folders.Keys];

    public IEnumerable<string> FileUpdatePaths => Updates.Where(u => u.Metadata is Models.File).Select(u => u.Path);

    public IEnumerable<string> FolderUpdatePaths => Updates.Where(u => u.Metadata is Models.Folder).Select(u => u.Path);

    public void SeedFile(string path, Models.File file) => _files[path] = file;

    public void SeedFolder(string path, Models.Folder folder) => _folders[path] = folder;

    /// <summary>
    /// The last <see cref="Models.File"/> DTO that was persisted to the given path, if any.
    /// </summary>
    public Models.File? FileUpdateAt(string path) => Updates.Where(u => u.Path == path).Select(u => u.Metadata).OfType<Models.File>().LastOrDefault();

    /// <summary>
    /// The last <see cref="Models.Folder"/> DTO that was persisted to the given path, if any.
    /// </summary>
    public Models.Folder? FolderUpdateAt(string path) => Updates.Where(u => u.Path == path).Select(u => u.Metadata).OfType<Models.Folder>().LastOrDefault();

    public void Reset()
    {
        lock (_lock)
        {
            _updates.Clear();
        }
    }

    public Task<Dictionary<string, T>> GetAllAsync<T>(string driveId, CancellationToken cancellation = default) where T : class
    {
        if (typeof(T) == typeof(Models.File))
        {
            return Task.FromResult((Dictionary<string, T>)(object)new Dictionary<string, Models.File>(_files));
        }

        if (typeof(T) == typeof(Models.Folder))
        {
            return Task.FromResult((Dictionary<string, T>)(object)new Dictionary<string, Models.Folder>(_folders));
        }

        return Task.FromResult(new Dictionary<string, T>());
    }

    public Task<T> GetAsync<T>(string driveId, string path, CancellationToken cancellation = default) where T : class
    {
        if (typeof(T) == typeof(Models.File) && _files.TryGetValue(path, out var file))
        {
            return Task.FromResult((T)(object)file);
        }

        if (typeof(T) == typeof(Models.Folder) && _folders.TryGetValue(path, out var folder))
        {
            return Task.FromResult((T)(object)folder);
        }

        throw new KeyNotFoundException($"No {typeof(T).Name} metadata seeded at '{path}'.");
    }

    public Task ProvisionAsync(string driveId, Type type, CancellationToken cancellation = default) => Task.CompletedTask;

    public Task UpdateAsync<T>(string driveId, string path, T metadata, CancellationToken cancellation = default) where T : class
    {
        lock (_lock)
        {
            _updates.Add((driveId, path, metadata));
        }

        return Task.CompletedTask;
    }
}
