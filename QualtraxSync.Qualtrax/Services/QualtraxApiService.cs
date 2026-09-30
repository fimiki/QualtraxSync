using Microsoft.Extensions.Logging;
using QualtraxSync.Contracts.Models;
using QualtraxSync.Contracts.Services;
using QualtraxSync.Qualtrax.Client;
using QualtraxSync.Qualtrax.Service;
using System.Runtime.CompilerServices;

namespace QualtraxSync.Qualtrax.Services;

public class QualtraxApiService(ILogger<QualtraxApiService> logger, IApiClient api, IQualtraxItemService itemService) : IQualtraxApiService, IDisposable
{
    private readonly Lazy<DirectoryInfo> _tempDirectory = new(() => Directory.CreateTempSubdirectory($"QualtraxFiles_{Guid.NewGuid()}"));

    public async Task<QualtraxDocument?> GetQualtraxDocumentAsync(int id, CancellationToken cancellationToken = default)
    {
        var document = await api.GetDocumentAsync(id, cancellationToken);
        if (document == null)
        {
            return null;
        }

        return await itemService.ToDocumentAsync(document, cancellationToken);
    }

    public async IAsyncEnumerable<QualtraxRevision> GetQualtraxRevisionsAsync(DateTimeOffset publishedAfter, DateTimeOffset publishedBefore, QualtraxItem? Parent = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var after = publishedAfter.LocalDateTime;
        var before = publishedBefore.LocalDateTime;
        var folderId = Parent?.Id ?? QualtraxItemService.RootFolder.Id;

        foreach (var file in await api.GetFilesAsync(folderId, after, before, _tempDirectory, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Cancellation requested. Stopping document retrieval.");
                yield break;
            }

            yield return await itemService.ToRevisionAsync(file, after, before, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_tempDirectory.IsValueCreated)
        {
            try
            {
                _tempDirectory.Value.Delete(true);
                logger.LogDebug("Temporary directory '{TempDirectory}' deleted successfully.", _tempDirectory.Value.FullName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete temporary directory '{TempDirectory}'.", _tempDirectory.Value.FullName);
            }
        }
    }
}

