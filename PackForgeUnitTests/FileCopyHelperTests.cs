using PackForge.Core.Util;
using Serilog;
using Xunit;
using Xunit.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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

    public void Dispose() => Log.CloseAndFlush();

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
        string tempSource = CreateTempDirectory(out string sourceFile);
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = new(Path.GetFileNameWithoutExtension(sourceFile),
                            Path.GetExtension(sourceFile),
                            true);
        await FileCopyHelper.CopyFilesAsync(
            tempSource,
            tempTarget, [rule]);

        Assert.True(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public async Task CopyFilesAsync_IgnoresNonMatchingRules()
    {
        string tempSource = CreateTempDirectory(out string sourceFile);
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = new("nonmatch", ".txt", true);
        await FileCopyHelper.CopyFilesAsync(
            tempSource,
            tempTarget, [rule]);

        Assert.False(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public async Task CopyFilesAsync_MatchesWildcardRules()
    {
        string tempSource = CreateTempDirectory(out string sourceFile);
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        await FileCopyHelper.CopyFilesAsync(
            tempSource,
            tempTarget, [Rule.None]);

        Assert.True(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public async Task CopyFilesAsync_MatchesWildcardFilePath_WithSpecificExtension()
    {
        string tempSource = CreateTempDirectory(out string sourceFile, "file.match");
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = new("*", ".match", true);
        await FileCopyHelper.CopyFilesAsync(
            tempSource,
            tempTarget, [rule]);

        Assert.True(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public async Task CopyFilesAsync_MatchesWildcardType_WithSpecificFilePath()
    {
        string tempSource = CreateTempDirectory(out string sourceFile, "exactfile.abc");
        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = new("exactfile", "*", true);
        await FileCopyHelper.CopyFilesAsync(
            tempSource,
            tempTarget, [rule]);

        Assert.True(File.Exists(Path.Combine(tempTarget, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public async Task CopyFilesAsync_MatchesExactDirectoryRule()
    {
        string tempSource = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string dirName = "matchdir";
        string nestedDir = Path.Combine(tempSource, dirName);
        Directory.CreateDirectory(nestedDir);
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "file.txt"), "test");

        string tempTarget = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempTarget);

        Rule rule = new("matchdir", "directory", true);
        await FileCopyHelper.CopyFilesAsync(
            tempSource,
            tempTarget, [rule]);

        Assert.True(File.Exists(Path.Combine(tempTarget, dirName, "file.txt")));
    }

    [Fact]
    public async Task CopyFilesAsync_ExcludesSpecificDirectory_WhenWhitelistFalse()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string keepDir = Path.Combine(root, "keep");
        string skipDir = Path.Combine(root, "skip");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(skipDir);
        await File.WriteAllTextAsync(Path.Combine(keepDir, "a.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(skipDir, "b.txt"), "2");

        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(target);

        Rule rule = new("skip", "directory", false);
        await FileCopyHelper.CopyFilesAsync(
            root,
            target, [rule],
            CancellationToken.None
        );

        Assert.True(File.Exists(Path.Combine(target, "keep", "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(target, "skip")));
    }

    [Fact]
    public async Task CopyFilesAsync_CopiesJarInSpecificFolder_WithWildcardPathRule()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string example = Path.Combine(root, "example");
        Directory.CreateDirectory(example);
        await File.WriteAllTextAsync(Path.Combine(example, "one.jar"), "");
        await File.WriteAllTextAsync(Path.Combine(example, "two.txt"), "");

        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(target);

        Rule rule = new("example/*", ".jar", true);
        await FileCopyHelper.CopyFilesAsync(
            root,
            target, [rule],
            CancellationToken.None
        );

        Assert.True(File.Exists(Path.Combine(target, "example", "one.jar")));
        Assert.False(File.Exists(Path.Combine(target, "example", "two.txt")));
    }

    [Fact]
    public async Task CopyFilesAsync_ExcludesJarGlobally_WhenBlacklistRule()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "keep.txt"), "");
        await File.WriteAllTextAsync(Path.Combine(root, "skip.jar"), "");

        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(target);

        Rule rule = new("*", ".jar", false);
        await FileCopyHelper.CopyFilesAsync(
            root,
            target, [rule],
            CancellationToken.None
        );

        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(target, "skip.jar")));
    }

    [Fact]
    public async Task CopyFilesAsync_ExcludesEverything_WhenGlobalWildcardBlacklist()
    {
        string root = CreateTempDirectory(out string file);
        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(target);

        Rule rule = new("*", "*", false);
        await FileCopyHelper.CopyFilesAsync(
            root,
            target, [rule],
            CancellationToken.None
        );

        Assert.Empty(Directory.EnumerateFileSystemEntries(target, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CopyFilesAsync_CopiesOnlyMatchedSubdirectories_WithWildcardDirRule()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string parent = Path.Combine(root, "parent");
        string a = Path.Combine(parent, "a");
        string b = Path.Combine(parent, "b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        await File.WriteAllTextAsync(Path.Combine(a, "1.txt"), "");
        await File.WriteAllTextAsync(Path.Combine(b, "2.txt"), "");

        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(target);

        Rule rule = new("parent/*", "directory", true);
        await FileCopyHelper.CopyFilesAsync(
            root,
            target, [rule],
            CancellationToken.None
        );

        Assert.True(File.Exists(Path.Combine(target, "parent", "a", "1.txt")));
        Assert.True(File.Exists(Path.Combine(target, "parent", "b", "2.txt")));
    }

    [Fact]
    public async Task CopyFilesAsync_IgnoresInvalidDirectoryRule_ForFilePaths()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "example.jar"), "");

        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(target);

        Rule rule = new("example.jar", "*", true);
        await FileCopyHelper.CopyFilesAsync(
            root,
            target, [rule],
            CancellationToken.None
        );

        Assert.False(File.Exists(Path.Combine(target, "example.jar")));
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