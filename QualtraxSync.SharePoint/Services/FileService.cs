using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using QualtraxSync.Contracts.Services;
using QualtraxSync.SharePoint.Client;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;

namespace QualtraxSync.SharePoint.Services;

/// <summary>
/// Manages files and folders in a SharePoint drive using Microsoft Graph.
/// </summary>
public class FileService(
    IntegrationGraphClient graphClient,
    TimeProvider time,
    ILogger<FileService> logger) : IFileService
{
    private readonly ConcurrentDictionary<(string DriveId, string Path), (string? UploadUrl, DateTimeOffset Expiration)> _uploadSessions = new();
    private static DateTimeOffset _uploadSessionCacheExpiration = default;
    private static readonly Lock _uploadSessionCacheLock = new();

    public async Task<bool> UploadAsync(string driveId, string path, Stream contents, DateTimeOffset? created = null, CancellationToken cancellationToken = default)
    {
        int tries = 0;
        const int maxRetries = 3;
        while (tries++ < maxRetries)
        {
            try
            {
                return await UploadFileAsync(driveId, path, contents, created, cancellationToken);
            }
            catch (ServiceException ex) when (tries < maxRetries)
            {
                if (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), time, cancellationToken);

                    var requestInformation = graphClient.Drives[driveId].Root.ItemWithPath(path).ToGetRequestInformation();

                    if (await graphClient.RequestAdapter.SendAsync(requestInformation, DriveItem.CreateFromDiscriminatorValue, graphClient.GetDefaultErrorMapper(), cancellationToken) != null)
                    {
                        return true;
                    }
                    else
                    {
                        _uploadSessions.TryRemove((driveId, path), out _);
                    }
                }

                var delay = (int)(TimeSpan.FromSeconds(2 * tries) + TimeSpan.FromMilliseconds(new Random().Next(0, 1000))).TotalMilliseconds;
                logger.LogWarning(ex, "Upload attempt {Attempt} for '{Path}' in drive '{DriveId}' failed due to service error. Retrying... ({Delay}ms)", tries, path, driveId, delay);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), time, cancellationToken); // Exponential backoff with jitter
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Upload failed for '{Path}' in drive '{DriveId}' after {MaxRetries} attempts.", path, driveId, maxRetries);
                throw;
            }
        }

        return false;
    }

    public async Task<bool> MoveAsync(string driveId, string oldPath, string newPath, CancellationToken cancellationToken = default)
    {
        if (oldPath == newPath) return true;

        DriveItem? item = null;
        RequestInformation? findRequest = null;

        try
        {
            item = await graphClient.Drives[driveId].Root.ItemWithPath(oldPath).GetAsync(cancellationToken: cancellationToken);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            try
            {
                findRequest = graphClient.Drives[driveId].Root.ItemWithPath(newPath).ToGetRequestInformation();

                var found = await graphClient.RequestAdapter.SendAsync(findRequest, DriveItem.CreateFromDiscriminatorValue, graphClient.GetDefaultErrorMapper(), cancellationToken);
                if (found != null)
                {
                    return true;
                }
                else
                {
                    logger.LogWarning("Item at path '{Path}' in drive '{DriveId}' was not found for move to {NewPath}.", oldPath, driveId, newPath);
                    throw;
                }
            }
            catch
            {
                logger.LogWarning("Item at path '{Path}' in drive '{DriveId}' was not found for move to {NewPath}.", oldPath, driveId, newPath);
                throw;
            }   
        }

        if (item?.Id == null)
        {
            throw new InvalidOperationException($"Item at path '{oldPath}' in drive '{driveId}' was not found for move to {newPath}.");
        }

        var (newParentPath, newName) = SplitPath(newPath);
        DriveItem? newParent = null;

        try
        {
            newParent = string.IsNullOrEmpty(newParentPath)
                ? await graphClient.Drives[driveId].Root.GetAsync(cancellationToken: cancellationToken)
                : await graphClient.Drives[driveId].Root.ItemWithPath(newParentPath).GetAsync(cancellationToken: cancellationToken);
        }

        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            newParent = await CreateFolderAsync(driveId, newParentPath, null, cancellationToken) ?? throw new InvalidOperationException($"Failed to create parent item at path '{newParentPath}' in drive '{driveId}'.");     
        }

        if (newParent?.Id == null)
        {
            throw new InvalidOperationException($"Failed to retrieve or create parent item at path '{newParentPath}' in drive '{driveId}'.");
        }

        try
        {
            var update = new DriveItem
            {
                Name = newName,
                ParentReference = new ItemReference { Id = newParent.Id },
                AdditionalData = new Dictionary<string, object> { { "@microsoft.graph.conflictBehavior", "replace" } }
            };

            var moved = await graphClient.Drives[driveId].Items[item.Id].PatchAsync(update, cancellationToken: cancellationToken);

            return moved != null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to move item from path '{OldPath}' to '{NewPath}' in drive '{DriveId}'.", oldPath, newPath, driveId);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string driveId, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await graphClient.Drives[driveId].Root.ItemWithPath(path).DeleteAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound || ex.ResponseStatusCode == (int)HttpStatusCode.BadRequest) 
        {
            return true;
        }
        catch (Exception ex) 
        {
            logger.LogError(ex, "Failed to delete item at path '{Path}' in drive '{DriveId}'.", path, driveId);
            throw;
        }
    }

    public async Task<bool> CreateAsync(string driveId, string path, DateTimeOffset? created = null, CancellationToken cancellationToken = default)
    {
        var item = await CreateFolderAsync(driveId, path, created, cancellationToken);

        return item != null;
    }

    private async Task<DriveItem?> CreateFolderAsync(string driveId, string path, DateTimeOffset? created, CancellationToken cancellationToken)
    {
        var (parentPath, name) = SplitPath(path);
        DriveItem? parent = null;

        try
        {
            parent = string.IsNullOrEmpty(parentPath)
                ? await graphClient.Drives[driveId].Root.GetAsync(cancellationToken: cancellationToken)
                : await graphClient.Drives[driveId].Root.ItemWithPath(parentPath).GetAsync(cancellationToken: cancellationToken);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound) { }

        if (parent?.Id == null)
        {
            parent = await CreateFolderAsync(driveId, parentPath, created, cancellationToken) ?? throw new InvalidOperationException($"Failed to create parent item at path '{parentPath}' in drive '{driveId}'.");
        }

        try
        { 
            var newItem = new DriveItem
            {
                Name = name,
                Folder = new Folder { },
                AdditionalData = new Dictionary<string, object>
                {
                    { "@microsoft.graph.conflictBehavior", "replace" }
                }
            };

            if (created.HasValue)
            {
                newItem.FileSystemInfo = new Microsoft.Graph.Models.FileSystemInfo
                {
                    CreatedDateTime = created.Value,
                    LastModifiedDateTime = created.Value,
                };
            }

            return await graphClient.Drives[driveId].Items[parent.Id].Children.PostAsync(newItem, cancellationToken: cancellationToken);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Conflict)
        {
            return await graphClient.Drives[driveId].Root.ItemWithPath(path).GetAsync(cancellationToken: cancellationToken);
        }
    }

    private async Task<bool> UploadFileAsync(string driveId, string path, Stream contents, DateTimeOffset? created = null, CancellationToken cancellationToken = default)
    {
        var sessionKey = (driveId, path);

        if (contents.Length == 0)
        {
            var zeroSizeItem = await graphClient.Drives[driveId].Root.ItemWithPath(path).Content.PutAsync(contents, null, cancellationToken) ?? throw new Exception($"Upload failed for {path} in drive {driveId}.");

            if (created.HasValue)
            {
                zeroSizeItem.FileSystemInfo = new Microsoft.Graph.Models.FileSystemInfo
                {
                    CreatedDateTime = created.Value,
                    LastModifiedDateTime = created.Value,
                };

                zeroSizeItem = await graphClient.Drives[driveId].Items[zeroSizeItem.Id].PatchAsync(zeroSizeItem, null, cancellationToken) ?? throw new Exception($"Upload failed for {path} in drive {driveId}.");
            }

            return true;
        }

        var uploadUrl = GetCachedUploadSessionUrl(sessionKey);
        var uploadSession = uploadUrl != null
            ? new UploadSession { UploadUrl = uploadUrl, NextExpectedRanges = [] }
            : null;

        if (uploadSession == null)
        {
            var uploadSessionRequestBody = new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object> { { "@microsoft.graph.conflictBehavior", "replace" } }
                }
            };

            if (created.HasValue)
            {
                uploadSessionRequestBody.Item.FileSystemInfo = new Microsoft.Graph.Models.FileSystemInfo
                {
                    CreatedDateTime = created.Value,
                    LastModifiedDateTime = created.Value,
                };
            }

            var uploadSessionRequest = graphClient.Drives[driveId]
                .Root
                .ItemWithPath(path)
                .CreateUploadSession
                .ToPostRequestInformation(uploadSessionRequestBody);

            using (var reader = new StreamReader(uploadSessionRequest.Content))
            {
                var jsonString = await reader.ReadToEndAsync();
                JsonNode? rootNode = JsonNode.Parse(jsonString);

                if (rootNode != null && rootNode["item"] is JsonObject itemObj)
                {
                    JsonNode? fileSystemInfoNode = itemObj["fileSystemInfo"];
                    JsonNode? conflictBehaviorNode = itemObj["@microsoft.graph.conflictBehavior"];

                    if (fileSystemInfoNode != null && conflictBehaviorNode != null)
                    {
                        itemObj.Remove("fileSystemInfo");
                        itemObj.Remove("@microsoft.graph.conflictBehavior");
                        itemObj.Add("@microsoft.graph.conflictBehavior", conflictBehaviorNode);
                        itemObj.Add("fileSystemInfo", fileSystemInfoNode);
                    }

                    uploadSessionRequest.Content = new StringContent(rootNode.ToJsonString()).ReadAsStream();
                }
            }

            uploadSession = await graphClient.RequestAdapter.SendAsync(
                uploadSessionRequest,
                UploadSession.CreateFromDiscriminatorValue,
                graphClient.GetDefaultErrorMapper(),
                cancellationToken);

            if (uploadSession != null)
            {
                _uploadSessions[sessionKey] = (uploadSession.UploadUrl, uploadSession.ExpirationDateTime.GetValueOrDefault());
            }
        }
        else
        {
            logger.LogDebug("Reusing cached upload session for path '{Path}' in drive '{DriveId}'.", path, driveId);
        }

        // Max slice size must be a multiple of 320 KiB
        int maxSliceSize = 320 * 4096;
        var fileUploadTask = new LargeFileUploadTask<DriveItem>(uploadSession, contents, maxSliceSize, graphClient.RequestAdapter);
        var uploadResult = uploadUrl == null ? await fileUploadTask.UploadAsync(null, 10, cancellationToken) : await fileUploadTask.ResumeAsync(null, 10, cancellationToken);

        return uploadResult.UploadSucceeded;
    }


    /// <summary>
    /// Returns the cached upload session for the given drive/path if it exists and has not yet expired,
    /// removing (and discarding) it if it has expired.
    /// </summary>
    private string? GetCachedUploadSessionUrl((string DriveId, string Path) sessionKey)
    {
        var now = time.GetUtcNow();

        if (_uploadSessionCacheExpiration.Date < now.Date)
        {
            lock (_uploadSessionCacheLock)
            {
                // clean the cache at midnight UTC to avoid holding onto stale sessions for too long
                if (_uploadSessionCacheExpiration.Date < now.Date)
                {
                    _uploadSessionCacheExpiration = now.Date;

                    foreach (var key in _uploadSessions.Where(cached => cached.Value.Expiration < now).Select(cached => cached.Key))
                    {
                        _uploadSessions.TryRemove(key, out _);
                    }
                }
            }
        }

        if (!_uploadSessions.TryGetValue(sessionKey, out var cached)) return null;

        if (cached.Expiration is DateTimeOffset expiration && expiration > now)
        {
            return cached.UploadUrl;
        }

        _uploadSessions.TryRemove(sessionKey, out _);

        return null;
    }

    private static (string ParentPath, string Name) SplitPath(string path)
    {
        var trimmed = path.Trim('/');
        var lastSlash = trimmed.LastIndexOf('/');

        return lastSlash < 0
            ? (string.Empty, trimmed)
            : (trimmed[..lastSlash], trimmed[(lastSlash + 1)..]);
    }
}