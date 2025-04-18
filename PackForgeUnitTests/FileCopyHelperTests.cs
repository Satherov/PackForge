using PackForge.Core.Util;
using Serilog;
using Xunit.Abstractions;

namespace PackForgeUnitTests;

public class FileCopyHelperTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public FileCopyHelperTests(ITestOutputHelper output)
    {
        _output = output;
            
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message}{NewLine}{Exception}")
            .CreateLogger();
    }

    public void Dispose()
    {
        Log.CloseAndFlush();
    }

    [Fact]
    public async Task CopyFilesAsync_NullRuleSet_CopiesAll()
    {
        string tempSource = CreateTempDirectory(out string sourceFile);
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        await FileCopyHelper.CopyFilesAsync(tempSource, tempTarget, null);

        Assert.True(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public async Task CopyFilesAsync_Cancellation_Throws()
    {
        string tempSource = CreateTempDirectory(out _);
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            FileCopyHelper.CopyFilesAsync(tempSource, tempTarget, null, cts.Token));
    }

    [Fact]
    public async Task CopyFilesAsync_AppliesExactFileMatchRule()
    {
        string tempSource = CreateTempDirectory(out string? sourceFile);
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = new(Path.GetFileNameWithoutExtension(sourceFile), Path.GetExtension(sourceFile), true) ;
        await FileCopyHelper.CopyFilesAsync(tempSource, tempTarget, [rule]);

        Assert.True(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public async Task CopyFilesAsync_IgnoresNonMatchingRules()
    {
        string tempSource = CreateTempDirectory(out string? sourceFile);
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = new("nonmatch", ".txt", true);
        await FileCopyHelper.CopyFilesAsync(tempSource, tempTarget, [rule]);

        Assert.False(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public async Task CopyFilesAsync_MatchesWildcardRules()
    {
        string tempSource = CreateTempDirectory(out string? sourceFile);
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = Rule.Empty;
        await FileCopyHelper.CopyFilesAsync(tempSource, tempTarget, [rule]);

        Assert.True(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }
    
    [Fact]
    public async Task CopyFilesAsync_MatchesWildcardFilePath_WithSpecificExtension()
    {
        string tempSource = CreateTempDirectory(out string? sourceFile, "file.match");
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = new("*", ".match", true);
        await FileCopyHelper.CopyFilesAsync(tempSource, tempTarget, [rule]);

        Assert.True(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public async Task CopyFilesAsync_MatchesWildcardType_WithSpecificFilePath()
    {
        string tempSource = CreateTempDirectory(out string? sourceFile, "exactfile.abc");
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = new("exactfile", "*", true);
        await FileCopyHelper.CopyFilesAsync(tempSource, tempTarget, [rule]);

        Assert.True(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public async Task CopyFilesAsync_MatchesExactDirectoryRule()
    {
        string tempSource = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string dirName = "matchdir";
        string nestedDir = Path.Combine(tempSource, dirName);
        Directory.CreateDirectory(nestedDir);
        string testFile = Path.Combine(nestedDir, "file.txt");
        await File.WriteAllTextAsync(testFile, "test");

        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = new("matchdir", "directory", true);
        await FileCopyHelper.CopyFilesAsync(tempSource, tempTarget, [rule]);

        Assert.True(File.Exists(Path.Combine(tempTarget, dirName, "file.txt")));
    }


    private static string CreateTempDirectory(out string testFilePath, string fileName = "testfile.txt")
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        testFilePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(testFilePath, "test");
        return tempDir;
    }
}