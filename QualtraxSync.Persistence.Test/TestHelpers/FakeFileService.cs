using QualtraxSync.Contracts.Services;

namespace QualtraxSync.Persistence.Test.TestHelpers;

/// <summary>
/// An in-memory stand-in for the SharePoint file service that records every operation so tests can assert on the
/// paths that were created, uploaded, moved and deleted.
/// </summary>
public sealed class FakeFileService : IFileService
{
    private readonly List<(string DriveId, string Path)> _creates = [];
    private readonly List<(string DriveId, string Path)> _deletes = [];
    private readonly List<(string DriveId, string Path, string Contents)> _uploads = [];
    private readonly List<(string DriveId, string OldPath, string NewPath)> _moves = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<(string DriveId, string Path)> Creates => Snapshot(_creates);

    public IReadOnlyList<(string DriveId, string Path)> Deletes => Snapshot(_deletes);

    public IReadOnlyList<(string DriveId, string Path, string Contents)> Uploads => Snapshot(_uploads);

    public IReadOnlyList<(string DriveId, string OldPath, string NewPath)> Moves => Snapshot(_moves);

    public IEnumerable<string> DeletedPaths => Deletes.Select(d => d.Path);

    public IEnumerable<string> UploadedPaths => Uploads.Select(u => u.Path);

    public bool Moved(string oldPath, string newPath) => Moves.Any(m => m.OldPath == oldPath && m.NewPath == newPath);

    public string? MovedTo(string oldPath) => Moves.Where(m => m.OldPath == oldPath).Select(m => m.NewPath).LastOrDefault();

    public void Reset()
    {
        lock (_lock)
        {
            _creates.Clear();
            _deletes.Clear();
            _uploads.Clear();
            _moves.Clear();
        }
    }

    public Task<bool> CreateAsync(string driveId, string path, DateTimeOffset? created = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _creates.Add((driveId, path));
        }

        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string driveId, string path, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _deletes.Add((driveId, path));
        }

        return Task.FromResult(true);
    }

    public async Task<bool> UploadAsync(string driveId, string path, Stream contents, DateTimeOffset? created = null, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(contents);
        var text = await reader.ReadToEndAsync(cancellationToken);

        lock (_lock)
        {
            _uploads.Add((driveId, path, text));
        }

        return true;
    }

    public Task<bool> MoveAsync(string driveId, string oldPath, string newPath, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _moves.Add((driveId, oldPath, newPath));
        }

        return Task.FromResult(true);
    }

    private IReadOnlyList<T> Snapshot<T>(List<T> source)
    {
        lock (_lock)
        {
            return [.. source];
        }
    }
}
