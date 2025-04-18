using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace PackForge.Core.Util;

public static class FileCopyHelper
{
    public static async Task CopyFilesAsync(string sourceRoot, string targetRoot, List<Rule>? ruleSet, CancellationToken ct = default)
    {
        sourceRoot = sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        targetRoot = targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        ruleSet ??= [Rule.Empty];
        ConcurrentBag<string> matchedDirs = [];
        List<(string sourcePath, string targetPath)> filesToCopy = [];

        foreach (string entry in Directory.EnumerateFileSystemEntries(sourceRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            string relativePath = Path.GetRelativePath(sourceRoot, entry);
            string targetPath = Path.Combine(targetRoot, relativePath);

            if (IsInMatchedDirectory(entry, matchedDirs) || IsIncluded(entry, ruleSet, matchedDirs)) filesToCopy.Add((entry, targetPath));
        }

        Parallel.ForEach(filesToCopy, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct }, pair =>
        {
            try
            {
                if (Directory.Exists(pair.sourcePath))
                {
                    Directory.CreateDirectory(pair.targetPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(pair.targetPath)!);
                    File.Copy(pair.sourcePath, pair.targetPath, true);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to copy {pair.sourcePath} to {pair.targetPath}: {ex.Message}");
            }
        });
    }

    private static bool IsIncluded(string fullPath, List<Rule> ruleSet, ConcurrentBag<string> matchedDirs)
    {
        string fileName = Path.GetFileName(fullPath);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(fullPath);
        string ext = Path.GetExtension(fullPath).TrimStart('.');
        bool isDir = File.GetAttributes(fullPath).HasFlag(FileAttributes.Directory);

        foreach (Rule rule in ruleSet)
        {
            string ruleFilePath = rule.FilePath;
            string ruleType = rule.Type.TrimStart('.');

            if (rule is { FilePath: "*", Type: "*" })
                return true;

            if (isDir)
            {
                if ((ruleFilePath != "*" && ruleFilePath != fileName) || ruleType != "directory") continue;

                matchedDirs.Add(fullPath);
                return true;
            }
            else
            {
                bool nameMatches = ruleFilePath == "*" || ruleFilePath == nameWithoutExt;
                bool extMatches = ruleType == "*" || ruleType.Equals(ext, StringComparison.OrdinalIgnoreCase);

                if (nameMatches && extMatches)
                    return true;
            }
        }

        return false;
    }

    private static bool IsInMatchedDirectory(string fullPath, ConcurrentBag<string> matchedDirs)
    {
        return matchedDirs.Any(dir => fullPath.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
}