using System.Reflection;

namespace QualtraxSync.SharePoint.Test;

public class FileServiceTests
{
    [Theory]
    [InlineData("folder/sub/file.txt", "folder/sub", "file.txt")]
    [InlineData("/folder/file.txt/", "folder", "file.txt")]
    [InlineData("file.txt", "", "file.txt")]
    [InlineData("/file.txt", "", "file.txt")]
    public void SplitPath_ReturnsExpectedParentAndName(string path, string expectedParentPath, string expectedName)
    {
        var (parentPath, name) = InvokeSplitPath(path);

        Assert.Equal(expectedParentPath, parentPath);
        Assert.Equal(expectedName, name);
    }

    private static (string ParentPath, string Name) InvokeSplitPath(string path)
    {
        var method = typeof(Services.FileService)
            .GetMethod("SplitPath", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SplitPath method not found.");

        var result = method.Invoke(null, [path]) ?? throw new InvalidOperationException("SplitPath returned null.");

        return ((string, string))result;
    }
}
