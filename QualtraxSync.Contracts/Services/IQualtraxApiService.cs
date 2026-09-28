using QualtraxSync.Contracts.Models;

namespace QualtraxSync.Contracts.Services;

public interface IQualtraxApiService
{
    /// <summary>
    /// Returns a QualtraxDocument by its ID, or null if not found
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The <see cref="QualtraxDocument"/> if found, otherwise null.  Contents are not included.</returns>
    Task<QualtraxDocument?> GetQualtraxDocumentAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a list of QualtraxDocuments with contents that were published between the given dates, optionally filtered by a parent.
    /// </summary>
    /// <param name="publishedAfter"></param>
    /// <param name="publishedBefore"></param>
    /// <param name="Parent"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    IAsyncEnumerable<QualtraxRevision> GetQualtraxRevisionsAsync(DateTimeOffset publishedAfter, DateTimeOffset publishedBefore, QualtraxItem? Parent = null, CancellationToken cancellationToken = default);
}
