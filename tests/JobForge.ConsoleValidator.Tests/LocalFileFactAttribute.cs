using Xunit;

namespace JobForge.ConsoleValidator.Tests;

/// <summary>
/// A [Fact] that runs only when a local, gitignored file exists under the repo's .files/ folder.
/// When the file is missing the test is reported as Skipped instead of failing.
/// </summary>
public sealed class LocalFileFactAttribute : FactAttribute
{
    public LocalFileFactAttribute(string fileName)
    {
        var path = LocalFiles.PathOf(fileName);
        if (path is null || !File.Exists(path))
        {
            Skip = $"Local file '.files/{fileName}' not found. Generate it to run this test.";
        }
    }
}

public static class LocalFiles
{
    public static string? PathOf(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobForge.slnx")))
        {
            dir = dir.Parent;
        }

        return dir is null ? null : Path.Combine(dir.FullName, ".files", fileName);
    }
}
