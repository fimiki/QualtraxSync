using Microsoft.Extensions.Logging;
using QualtraxSync.Qualtrax.Models;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QualtraxSync.Qualtrax.Client;

public interface IApiClient
{
    Task<Document?> GetDocumentAsync(int documentId, CancellationToken cancellationToken);
    Task<IEnumerable<FileInfo>> GetFilesAsync(int folderId, DateTime publishedAfter, DateTime publishedBefore, Lazy<DirectoryInfo> downloadTo, CancellationToken cancellationToken);
    Task<Folder> GetFolderAsync(int folderId, CancellationToken cancellationToken);
}

public class ApiClient(HttpClient httpClient, ILogger<ApiClient> logger, TimeProvider time) : IApiClient

{
    internal const string Name = "Qualtrax";
    internal const int FilesTimeoutSeconds = 600;

    internal HttpClient Http { get; } = httpClient;

    public virtual async Task<Document?> GetDocumentAsync(int documentId, CancellationToken cancellationToken)
    {
        var response = await Http.GetAsync($"api/documents/{documentId}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
            response.StatusCode == System.Net.HttpStatusCode.Gone ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden) return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(ApiSerializationContext.Default.Document, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to deserialize document with ID '{documentId}'.");
    }

    public virtual async Task<Folder> GetFolderAsync(int folderId, CancellationToken cancellationToken)
    {
        var response = await Http.GetAsync($"api/folders/{folderId}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return Folder.GetUnknownForId(folderId);

        response.EnsureSuccessStatusCode();

        var folder = await response.Content.ReadFromJsonAsync(ApiSerializationContext.Default.Folder, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to deserialize folder with ID '{folderId}'.");

        logger.LogDebug("Retrieved folder {Name} with ID '{FolderId}' from Ideagen API.", folder.Title, folderId);

        return folder;
    }

    public virtual async Task<IEnumerable<FileInfo>> GetFilesAsync(int folderId, DateTime publishedAfter, DateTime publishedBefore, Lazy<DirectoryInfo> downloadTo, CancellationToken cancellationToken)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(FilesTimeoutSeconds));
        cts.Token.Register(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cts.Cancel();
            }
        });

        var timestamp = time.GetTimestamp();

        HttpResponseMessage response;

        var start = publishedAfter.ToString("o");

        // The Ideagen API often seems to time out when the end of the range is exactly midnight, so we subtract a small amount of time to avoid that.
        var end = publishedBefore.TimeOfDay == TimeSpan.Zero ? (publishedBefore - TimeSpan.FromMilliseconds(10)).ToString("o") : publishedBefore.ToString("o");

        var requestUri = $"api/files?folderId={folderId}&activeDateRange={start} .. {end}&publishedMode=1";

        try
        {
            response = await Http.GetAsync(requestUri, cts.Token);
        }
        catch (TaskCanceledException) when (time.GetElapsedTime(timestamp) >= TimeSpan.FromSeconds(FilesTimeoutSeconds))
        {
            throw new QualtraxFilesTimeoutException($"Request for files in folder ID '{folderId}' between '{publishedAfter}' and '{publishedBefore}' timed out after {time.GetElapsedTime(timestamp).TotalSeconds} seconds.");
        }

        var elapsedTime = time.GetElapsedTime(timestamp);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            logger.LogDebug("No files found for folder ID '{FolderId}' between '{ModifiedAfter}' and '{ModifiedBefore}' after {Time} seconds", folderId, publishedAfter, publishedBefore, elapsedTime.TotalSeconds);

            return [];
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new KeyNotFoundException($"No files found for folder ID '{folderId}'.");

        response.EnsureSuccessStatusCode();

        await using var zipStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        if (zipArchive.Entries.Count == 0) return [];

        logger.LogDebug("Found {FileCount} files for folder ID '{FolderId}' modified between '{ModifiedAfter}' and '{ModifiedBefore}' in {Time} seconds", zipArchive.Entries.Count, folderId, publishedAfter, publishedBefore, elapsedTime.TotalSeconds);

        var tempDirectory = downloadTo.Value;
        var folderDirectory = tempDirectory.CreateSubdirectory(publishedAfter.ToString("s").Replace(':', '-') + "-" + folderId + "-" + zipArchive.Entries.Count + "-" + Random.Shared.Next());

        zipArchive.ExtractToDirectory(folderDirectory.FullName);

        return folderDirectory.EnumerateFiles("*", SearchOption.AllDirectories);
    }
}

public sealed class QualtraxFilesTimeoutException(string message) : TimeoutException(message);

[JsonSerializable(typeof(Folder))]
[JsonSerializable(typeof(Document))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class ApiSerializationContext : JsonSerializerContext;