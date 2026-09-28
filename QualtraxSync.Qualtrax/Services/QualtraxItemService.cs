using Microsoft.Extensions.Logging;
using Microsoft.IO;
using QualtraxSync.Contracts.Models;
using QualtraxSync.Qualtrax.Models;
using QualtraxSync.Qualtrax.Client;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace QualtraxSync.Qualtrax.Service;

public interface IQualtraxItemService
{
    Task<QualtraxDocument> ToDocumentAsync(Document document, CancellationToken cancellationToken);
    Task<QualtraxRevision> ToRevisionAsync(FileInfo file, DateTime after, DateTime before, CancellationToken cancellationToken);
}

public partial class QualtraxItemService(ILogger<QualtraxItemService> logger, IApiClient api, TimeProvider time) : IQualtraxItemService
{
    private readonly ConcurrentDictionary<int, Lazy<Task<QualtraxItem>>> _folders = new();
    private readonly RevisionCache _retiredRevisions = new(17179869184, time); // 16 GB
    private static DateTime _folderCacheExpiration = DateTime.MinValue;
    private static readonly RecyclableMemoryStreamManager _streamManager = new();

    private static readonly Regex _unicodeDeviceControlPattern = UnicodeControlString();
    private static readonly char[] _illegalChars = ['~', '#', '%', '&', '*', '{', '}', '\\', ':', '<', '>', '?', '/', '|', '"'];

    internal static readonly QualtraxItem RetiredFolder = new(-6, "Retired Documents", null);
    internal static readonly QualtraxItem RootFolder = new(-1, "Root", null);

    /// <summary>
    /// Converts a file downloaded from the Qualtrax API into to a QualtraxRevision record.
    /// </summary>
    /// <param name="file">The file to convert.</param>
    /// <param name="modifiedSince">The start of the modification date range.</param>
    /// <param name="modifiedBefore">The end of the modification date range.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task result containing the QualtraxRevision.</returns>
    public Task<QualtraxRevision> ToRevisionAsync(FileInfo file, DateTime modifiedSince, DateTime modifiedBefore, CancellationToken cancellationToken = default) => ToQualtraxRevision(file, modifiedSince, modifiedBefore, 0, cancellationToken);

    /// <summary>
    /// Returns the latest information for a Qualtrax document from the API and converts it to a QualtraxDocument record. 
    /// </summary>
    /// <param name="document">The document to convert.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task result containing the QualtraxDocument.</returns>
    public async Task<QualtraxDocument> ToDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        var parent = await GetParentAsync(document.ParentId, document.ParentType.Equals("Document"), cancellationToken);

        return new QualtraxDocument(
            document.Id,
            document.Revision,
            Normalize(document.Title),
            parent,
            GetQualtraxStatus(document.Status),
            document.DateCreated);
    }

    private async Task<QualtraxRevision> ToQualtraxRevision(FileInfo file, DateTimeOffset after, DateTimeOffset before, int tries = 3, CancellationToken cancellationToken = default)
    {
        var parts = file.Name[..^file.Extension.Length].Split('-');
        var documentId = int.Parse(parts[^2]);
        var revisionId = int.Parse(parts[^1]);
        var retired = file.DirectoryName?.EndsWith("Retired", StringComparison.OrdinalIgnoreCase) is true;

        // Retired revisions are returned by the Qualtrax API from the day before they were published through the day they were superceeded, so cache the first instance to avoid repeat processing
        var toRevision = retired ? _retiredRevisions.GetOrAddRevision(documentId, revisionId, file.Length, ToRevisionAsync) : ToRevisionAsync();

        _retiredRevisions.RemoveExpiredEntires();

        try
        {
            var revision = await toRevision;

            if (!revision.Contents.CanRead)
            {
                revision = revision with { Contents = _streamManager.GetStream() };

                using var fileStream = file.OpenRead();
                await fileStream.CopyToAsync(revision.Contents, cancellationToken);
                revision.Contents.Position = 0;
            }

            file.Delete();

            return revision;
        }
        catch
        {
            if (retired) _retiredRevisions.TryRemoveRevision(documentId, revisionId);
            if (tries++ > 3)
            {
                file.Delete();
                throw;
            }

            await Task.Delay(TimeSpan.FromSeconds(2*tries), cancellationToken);

            return await ToQualtraxRevision(file, after, before, tries, cancellationToken);
        }

        async Task<QualtraxRevision> ToRevisionAsync()
        {
            using var fileStream = file.OpenRead();
            var documentStream = _streamManager.GetStream();
            var fileCopy = fileStream.CopyToAsync(documentStream, cancellationToken);

            var document = await api.GetDocumentAsync(documentId, cancellationToken);
            var parent = document == null ? null : await ToDocumentAsync(document, cancellationToken);

            if (parent == null)
            {
                var title = Normalize(string.Concat(string.Join('-', parts.Take(parts.Length - 2))));

                parent = new QualtraxDocument(documentId, revisionId, title, RetiredFolder, QualtraxStatus.Retired, file.CreationTime);

                logger.LogWarning("Metadata for Qualtrax document {Id} {Title} {Revision} could not be accessed by the API. Assigning to retired folder", documentId, title, revisionId);
            }

            var extension = file.Extension.TrimStart('.');
            var current = revisionId == parent.LatestRevision && parent.Status != QualtraxStatus.Retired;

            // Retired documents show up a day earlier than when they were originally published, so we need to adjust the created date accordingly.
            // Use the document's creation date if it is after the current window, otherwise we use start of the current window
            var published = new[] { retired ? after.AddDays(1) : after, parent.Created }.Max();

            // If this is the latest revision of an active document, we can get a more precise publish date by using the last write time
            if (current && after <= file.LastWriteTime && file.LastWriteTime <= before)
            {
                published = file.LastWriteTime;
            }

            // If this is not the latest revision of an active document it has been archived and we can pass the last write time as the archival date
            DateTimeOffset? archived = current ? null : file.LastWriteTime;

            await fileCopy;

            documentStream.Position = 0;

            return new QualtraxRevision(
                revisionId,
                parent,
                extension,
                documentStream,
                published,
                archived);
        }
    }

    /// <summary>
    /// Returns the current Qualtrax document status based on the "Status" field from the document API
    /// </summary>
    /// <param name="status"></param>
    /// <returns></returns>
    private static QualtraxStatus GetQualtraxStatus(string status)
    {
        return status switch
        {
            "Published" => QualtraxStatus.Published,
            "Retired" => QualtraxStatus.Retired,
            _ => QualtraxStatus.Other
        };
    }

    /// <summary>
    /// Returns the Qualtrax document status as-of the date the file was downloaded.
    /// </summary>
    /// <param name="file"></param>
    /// <param name="after"></param>
    /// <param name="before"></param>
    /// <returns></returns>
    public static QualtraxStatus GetQualtraxStatus(FileInfo file, DateTimeOffset after, DateTimeOffset before)
    {
        if (file.DirectoryName?.EndsWith("Retired", StringComparison.OrdinalIgnoreCase) is true
            && after <= file.LastWriteTime && file.LastWriteTime <= before)
        {
            return QualtraxStatus.Retired;
        }

        return QualtraxStatus.Published;
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // Replace escaped C1 control tokens like "\u0090"
        name = _unicodeDeviceControlPattern.Replace(name, " ");

        // Replace actual in-string C1 control characters (U+0080..U+009F)
        name = string.Concat(name.Select(c => c is >= '\u0080' and <= '\u009F' ? ' ' : c));

        // Replace illegal characters with a dash
        name = string.Concat(name.Select(c => _illegalChars.Contains(c) ? " - " : c.ToString())).Trim();

        // Trim all double spaces to single spaces
        while (name.Contains("  "))
        {
            name = name.Replace("  ", " ");
        }

        return name;
    }

    private async Task<QualtraxItem> GetParentAsync(int parentId, bool parentIsDocument, CancellationToken cancellationToken = default)
    {
        var now = time.GetLocalNow();

        // Expire the folder cache at midnight so any changes made to names or hierarchy will be reflected in the next day
        if (_folderCacheExpiration.Date < now.Date)
        {
            _folders.Clear();
            _folderCacheExpiration = now.Date;
        }

        var item = _folders.GetOrAdd(parentId, id => new Lazy<Task<QualtraxItem>>(async () =>
        {
            QualtraxItem record;

            if (parentIsDocument)
            {
                var document = await api.GetDocumentAsync(id, cancellationToken);
                record = document == null ? RetiredFolder : await ToDocumentAsync(document, cancellationToken);
            }
            else
            {
                var folder = await api.GetFolderAsync(id, cancellationToken);
                var title = Normalize(folder.Title);

                if (folder.ParentId > 0)
                {
                    var parent = await GetParentAsync(folder.ParentId, false, cancellationToken);
                    record = new QualtraxItem(folder.Id, title, parent);

                }
                else
                {
                    record = new QualtraxItem(folder.Id, title, null);
                }
            }

            return record;
        })).Value;

        try
        {
            return await item;
        }
        catch
        {
            _folders.Remove(parentId, out _);

            throw;
        }
    }

    [GeneratedRegex(@"\\u00(?:8[0-9A-Fa-f]|9[0-9A-Fa-f])", RegexOptions.Compiled)]
    private static partial Regex UnicodeControlString();

    private class RevisionCache(long sizeLimit, TimeProvider time) : ConcurrentDictionary<RevisionCache.RevisionCacheKey, RevisionCache.RevisionCacheValue>
    {
        private readonly long _sizeLimit = sizeLimit;
        private long _currentSize = 0;

        public Task<QualtraxRevision> GetOrAddRevision(int id, int revisionId, long size, Func<Task<QualtraxRevision>> getRevision)
        {
            TrimToFit(size);

            var key = new RevisionCacheKey(id, revisionId);
            var value = GetOrAdd(key, _ => new RevisionCacheValue(getRevision, size, time));

            value.Refresh();

            return value.Revision.Value;
        }

        public void TryRemoveRevision(int id, int revisionId) => TryRemove(new RevisionCacheKey(id, revisionId), out _);

        public void RemoveExpiredEntires()
        {
            var now = time.GetLocalNow();

            foreach (var key in this.Where(v => now > v.Value.Expiration).Select(v => v.Key).ToList())
            {
                if (TryRemove(key, out var value))
                {
                    _currentSize -= value.Size;
                }
            }
        }

        public void TrimToFit(long size)
        {
            var target = _sizeLimit - size;
            if (_currentSize < target) return;
            if (size > _sizeLimit) throw new NotSupportedException($"Retired revision cache cannot hold a document of size {size} bytes");

            var now = time.GetLocalNow();

            foreach (var key in this.OrderBy(v => now > v.Value.Expiration).Select(v => v.Key).ToList())
            {
                if (TryRemove(key, out var value))
                {
                    _currentSize -= value.Size;
                }

                if (_currentSize < target) return;
            }
        }

        public record RevisionCacheKey(int Id, int Revision);

        public class RevisionCacheValue
        {
            private readonly TimeSpan _timeToLive = TimeSpan.FromHours(2);
            private readonly TimeProvider _time;

            public Lazy<Task<QualtraxRevision>> Revision { get; }
            public DateTimeOffset Expiration { get; private set; }
            public long Size { get; private set; }

            public RevisionCacheValue(Func<Task<QualtraxRevision>> getRevision, long size, TimeProvider time)
            {
                Revision = new Lazy<Task<QualtraxRevision>>(getRevision, LazyThreadSafetyMode.ExecutionAndPublication);
                Size = size;
                _time = time;
                Refresh();
            }

            public void Refresh() => Expiration = _time.GetLocalNow().Add(_timeToLive);
        }
    }
}
