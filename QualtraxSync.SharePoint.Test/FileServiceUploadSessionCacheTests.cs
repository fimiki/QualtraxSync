using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models;
using System.Collections.Concurrent;
using System.Reflection;

namespace QualtraxSync.SharePoint.Test;

/// <summary>
/// Tests the upload session caching logic in <see cref="Services.FileService"/> via
/// reflection, since the cache is a private implementation detail and no method exercising it is reachable
/// without issuing a real Graph request.
/// </summary>
public class FileServiceUploadSessionCacheTests
{
    private const string DriveId = "drive-1";
    private const string Path = "folder/file.txt";

    [Fact]
    public void GetCachedUploadSession_ReturnsNull_WhenNotCached()
    {
        var service = CreateService();

        var result = InvokeGetCachedUploadSession(service);

        Assert.Null(result);
    }

    [Fact]
    public void GetCachedUploadSession_ReturnsSession_WhenCachedAndNotExpired()
    {
        var service = CreateService();
        var session = new UploadSession { ExpirationDateTime = DateTimeOffset.UtcNow.AddMinutes(10), UploadUrl = "https://example.com/upload" };

        SeedCache(service, session);

        var result = InvokeGetCachedUploadSession(service);

        Assert.Equal(session.UploadUrl, result);
    }

    [Fact]
    public void GetCachedUploadSession_ReturnsNull_AndEvicts_WhenExpired()
    {
        var service = CreateService();
        var session = new UploadSession { ExpirationDateTime = DateTimeOffset.UtcNow.AddMinutes(-10) };

        SeedCache(service, session);

        var result = InvokeGetCachedUploadSession(service);

        Assert.Null(result);
        Assert.Null(InvokeGetCachedUploadSession(service));
    }

    private static Services.FileService CreateService() =>
        new(TestHelpers.CreateGraphClient(), TimeProvider.System, NullLogger<Services.FileService>.Instance);

    private static void SeedCache(Services.FileService service, UploadSession session)
    {
        var field = typeof(Services.FileService)
            .GetField("_uploadSessions", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_uploadSessions field not found.");

        var dictionary = (ConcurrentDictionary<(string DriveId, string Path), (string? Url, DateTimeOffset Expiration)>)field.GetValue(service)!;

        dictionary[(DriveId, Path)] = (session.UploadUrl, session.ExpirationDateTime.GetValueOrDefault());
    }

    private static string? InvokeGetCachedUploadSession(Services.FileService service)
    {
        var method = typeof(Services.FileService)
            .GetMethod("GetCachedUploadSessionUrl", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("GetCachedUploadSessionUrl method not found.");

        return (string?)method.Invoke(service, [(DriveId, Path)]);
    }
}
