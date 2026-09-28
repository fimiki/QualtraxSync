namespace QualtraxSync.Qualtrax.Test.TestHelpers;

/// <summary>
/// Manages a temp directory tree so file-based tests can create files with the
/// "{Title}-{DocumentId}-{RevisionId}{Extension}" naming convention expected by QualtraxItemService.
/// </summary>
internal sealed class TempFileScope : IDisposable
{
    public DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("QualtraxItemServiceTests_" + Guid.NewGuid());

    public FileInfo CreateFile(int documentId, int revisionId, string title = "Doc", string extension = ".pdf", bool retired = false, string contents = "content")
    {
        var directory = retired ? Root.CreateSubdirectory("Retired") : Root.CreateSubdirectory("Published");
        var path = Path.Combine(directory.FullName, $"{title}-{documentId}-{revisionId}{extension}");

        File.WriteAllText(path, contents);

        return new FileInfo(path);
    }

    public void Dispose()
    {
        try
        {
            if (Root.Exists)
            {
                Root.Delete(recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }
}
