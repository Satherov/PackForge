using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace PackForge.Core.Util
{
    public static class FileCopyHelper
    {
        public static async Task CopyFilesAsync(
            string sourceRoot,
            string targetRoot,
            List<Rule>? ruleSet,
            CancellationToken ct = default)
        {
            sourceRoot = sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            targetRoot = targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // default to “copy everything”
            ruleSet ??= [Rule.All];

            ConcurrentBag<string> includedDirs = [];
            ConcurrentBag<string> excludedDirs = [];
            List<(string sourcePath, string targetPath)> filesToCopy  = [];

            foreach (string fullPath in Directory.EnumerateFileSystemEntries(sourceRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                // if under an excluded directory, skip entirely
                if (IsUnderDirectory(fullPath, excludedDirs))
                    continue;

                // compute relative path for target and for rule-matching
                string relativePath = Path.GetRelativePath(sourceRoot, fullPath);
                string targetPath   = Path.Combine(targetRoot, relativePath);

                // if under an included directory, always copy
                // otherwise consult rules
                if (IsUnderDirectory(fullPath, includedDirs) 
                    || EvaluateInclusion(fullPath, relativePath, ruleSet, includedDirs, excludedDirs))
                {
                    filesToCopy.Add((fullPath, targetPath));
                }
            }

            await Task.Run(() =>
            {
                ParallelOptions po = new()
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken     = ct
                };

                Parallel.ForEach(filesToCopy, po, pair =>
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
                        Log.Warning("Failed to copy {PairSourcePath} → {PairTargetPath}: {ExMessage}", pair.sourcePath, pair.targetPath, ex.Message);
                    }
                });
            }, ct);
        }

        private static bool EvaluateInclusion(
            string fullPath,
            string relativePath,
            List<Rule> ruleSet,
            ConcurrentBag<string> includedDirs,
            ConcurrentBag<string> excludedDirs)
        {
            bool isDir   = File.GetAttributes(fullPath).HasFlag(FileAttributes.Directory);
            string ext   = Path.GetExtension(fullPath).TrimStart('.');
            bool hasWl   = ruleSet.Any(r => r.Whitelist);
            bool hasBl   = ruleSet.Any(r => !r.Whitelist);

            // normalize for matching
            string relNorm       = relativePath.TrimEnd(Path.DirectorySeparatorChar);
            string relNoExt      = isDir ? relNorm : Path.ChangeExtension(relNorm, null)!;

            foreach (Rule rule in ruleSet)
            {
                // normalize rule path
                string rawRuleFile   = rule.FilePath.Replace('/', Path.DirectorySeparatorChar);
                bool   isPathWildcard= rawRuleFile.Contains('*');
                string ruleType      = rule.Type.TrimStart('.');

                bool ruleMatches = false;

                switch (isDir)
                {
                    case true when ruleType.Equals("directory", StringComparison.OrdinalIgnoreCase):
                    {
                        // match directories
                        if (rawRuleFile == "*")
                        {
                            ruleMatches = true;
                        }
                        else if (isPathWildcard)
                        {
                            // e.g. "example/*" => prefix = "example"
                            int  idx    = rawRuleFile.IndexOf('*');
                            string prefix = rawRuleFile[..idx].TrimEnd(Path.DirectorySeparatorChar);
                            if (relNorm.StartsWith(prefix + Path.DirectorySeparatorChar,
                                    StringComparison.OrdinalIgnoreCase))
                                ruleMatches = true;
                        }
                        else
                        {
                            // exact path match
                            if (relNorm.Equals(rawRuleFile, StringComparison.OrdinalIgnoreCase))
                                ruleMatches = true;
                        }

                        if (ruleMatches)
                        {
                            Log.Debug("Rule matches: {Rule} for {FullPath}", rule, fullPath);
                            
                            if (rule.Whitelist)
                                includedDirs.Add(fullPath);
                            else
                                excludedDirs.Add(fullPath);

                            return rule.Whitelist;
                        }

                        break;
                    }
                    case false when rawRuleFile == "*" && ruleType == "*":
                        return rule.Whitelist;
                    case false:
                    {
                        bool extMatches = ruleType == "*" || ruleType.Equals(ext, StringComparison.OrdinalIgnoreCase);
                        bool pathMatches = false;

                        if (rawRuleFile == "*")
                        {
                            pathMatches = true;
                        }
                        else if (isPathWildcard)
                        {
                            int idx = rawRuleFile.IndexOf('*');
                            string prefix = rawRuleFile.Substring(0, idx).TrimEnd(Path.DirectorySeparatorChar);
                            if (relNorm.StartsWith(prefix + Path.DirectorySeparatorChar,
                                    StringComparison.OrdinalIgnoreCase))
                                pathMatches = true;
                        }
                        else
                        {
                            if (relNoExt.Equals(rawRuleFile, StringComparison.OrdinalIgnoreCase))
                                pathMatches = true;
                        }

                        if (pathMatches && extMatches)
                        {
                            return rule.Whitelist;
                        }

                        break;
                    }
                }
            }

            return !hasWl && hasBl;
        }

        private static bool IsUnderDirectory(string fullPath, ConcurrentBag<string> dirs)
        {
            return dirs.Any(d =>
                fullPath.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
             || string.Equals(fullPath, d, StringComparison.OrdinalIgnoreCase));
        }
    }
}
