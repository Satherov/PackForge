using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Newtonsoft.Json.Linq;
using PackForge.Core.Util;
using Serilog;
using Serilog.Events;
using JsonSerializer = System.Text.Json.JsonSerializer;

[assembly: InternalsVisibleTo("PackForgeUnitTests")]

namespace PackForge.Core.Builders;

public enum SectionType
{
    Recipes,
    Tags,
    Registries,
    LootTable,
    Unknown
}

public enum FileChangedType
{
    Added,
    Changed,
    Removed
}

public record FileEntry(string RelativePath, FileChangedType FileChangedType, string? PrettyPath = null);

public record ModEntry(string Name, string NewVersion, string OldVersion);

public static class ChangelogGenerator
{
    public static readonly string ChangelogPath = Path.Combine(App.AppDataPath, "data", "changelog");
    public static readonly string OldExportDir = Path.Combine(ChangelogPath, "export-old");
    public static readonly string NewExportDir = Path.Combine(ChangelogPath, "export-new");

    public static async Task GenerateFullChangelogAsync(string changelogPath, string? exportSourcePath, string version, string? oldVersion = null, CancellationToken ct = default)
    {
        if (!Validator.DirectoryExists(OldExportDir, null) && !Validator.DirectoryExists(NewExportDir, null) &&
            Validator.IsNullOrWhiteSpace(exportSourcePath, LogEventLevel.Debug) && oldVersion == null)
            return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        Log.Information("Starting changelog generation");
        Log.Debug($"Arguments: [Dest: {changelogPath}] [Source: {exportSourcePath}] [Version: {version}] [OldVersion: {oldVersion}]");
        Task copyTask = Task.CompletedTask;

        if (oldVersion == null)
        {
            if (!Validator.DirectoryExists(OldExportDir))
            {
                Log.Warning("Old export directory does not exist, if this is your first time running the generator, do another kubejs export and then run it again");
                await FileCopyHelper.CopyFilesAsync(exportSourcePath!, OldExportDir, null, ct);
                return;
            }

            if (!Validator.DirectoryExists(NewExportDir))
            {
                Log.Information("Collecting new export files");
                await FileCopyHelper.CopyFilesAsync(exportSourcePath!, NewExportDir, null, ct);
            }

            Log.Debug("Creating versioned export");
            copyTask = FileCopyHelper.CopyFilesAsync(NewExportDir, Path.Combine(ChangelogPath, $"export-{version}"), null, ct);
        }

        string oldExport = oldVersion == null ? OldExportDir : Path.Combine(ChangelogPath, $"export-{oldVersion}");
        string newExport = oldVersion == null ? NewExportDir : Path.Combine(ChangelogPath, $"export-{version}");

        Log.Debug($"Old export: {oldExport}");
        Log.Debug($"New export: {newExport}");

        StringBuilder sb = new();

        sb.AppendLine("# Changelog\n");
        sb.AppendLine($"# 🎞️ {version}\n");
        sb.AppendLine("## 📰 General changes and notes\n");
        sb.AppendLine("Summary of changes here!\n");
        sb.AppendLine("---\n");

        List<ModEntry> modsDiff = await GetModsDiffAsync(oldExport, newExport, ct);
        AppendModsSection(sb, modsDiff);

        Log.Debug("Collecting files");
        Task<Dictionary<string, string>> oldTask =
            Task.Run(
                () => Directory.EnumerateFiles(oldExport, "*.json", SearchOption.AllDirectories)
                    .Where(f => Path.GetRelativePath(oldExport, f).Contains(Path.DirectorySeparatorChar)).ToDictionary(f => Path.GetRelativePath(oldExport, f), f => f), ct);

        Task<Dictionary<string, string>> newTask =
            Task.Run(
                () => Directory.EnumerateFiles(newExport, "*.json", SearchOption.AllDirectories)
                    .Where(f => Path.GetRelativePath(newExport, f).Contains(Path.DirectorySeparatorChar)).ToDictionary(f => Path.GetRelativePath(newExport, f), f => f), ct);

        await Task.WhenAll(oldTask, newTask);

        List<FileEntry> fileDiffs = await GetFolderDiffAsync(oldExport, newExport, oldTask.Result, newTask.Result, ct);
        Dictionary<SectionType, List<FileEntry>> sectionMap = GroupBySection(fileDiffs);

        Log.Information($"Writing changelog entries to file");
        foreach ((SectionType section, List<FileEntry> entries) in sectionMap.OrderBy(x => x.Key))
        {
            Log.Debug($"Writing section: '{section}'");
            if (entries.Count == 0)
            {
                Log.Debug($"No entries for section: '{section}'");
                continue;
            }

            sb.AppendLine($"## {PrintSectionHeader(section)}\n");

            foreach (FileChangedType changeType in Enum.GetValues<FileChangedType>())
            {
                List<FileEntry> group = entries.Where(e => e.FileChangedType == changeType).OrderBy(e => e.RelativePath).ToList();
                if (group.Count == 0) continue;

                sb.AppendLine("<details>");
                sb.AppendLine($"<summary>{changeType} ({group.Count})</summary>");
                sb.AppendLine("<blockquote>\n");

                foreach (string entryText in group.Select(entry => BuildEntry(oldExport, newExport, entry, section)).Where(entryText => !string.IsNullOrEmpty(entryText)))
                    sb.AppendLine(entryText);

                sb.AppendLine("</blockquote>");
                sb.AppendLine("</details>\n");
            }
        }

        await File.WriteAllTextAsync(Path.Combine(changelogPath, $"CHANGELOG-{version}.md"), sb.ToString(), ct);
        Log.Information($"Changelog generated successfully after {stopwatch.ElapsedMilliseconds}ms");

        Log.Debug("Waiting for versioned export to finish");
        await copyTask;
        Log.Debug("Cleaning up old export directories");
        Directory.Delete(OldExportDir, true);
        Directory.Move(NewExportDir, OldExportDir);
    }

    internal static async Task<List<ModEntry>> GetModsDiffAsync(string oldExport, string newExport, CancellationToken ct = default)
    {
        string oldModsPath = Path.Combine(oldExport, "mods.json");
        string newModsPath = Path.Combine(newExport, "mods.json");

        if (!Validator.FileExists(oldModsPath) || !Validator.FileExists(newModsPath)) return [];

        Log.Debug("Generating mods differences");
        Log.Debug($"Old path: {oldModsPath}");
        Log.Debug($"New path: {newModsPath}");

        List<Dictionary<string, string>> oldMods = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(await File.ReadAllTextAsync(oldModsPath, ct)) ?? [];
        List<Dictionary<string, string>> newMods = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(await File.ReadAllTextAsync(newModsPath, ct)) ?? [];

        Dictionary<string, Dictionary<string, string>> oldDict = oldMods.ToDictionary(m => m["id"]);
        Dictionary<string, Dictionary<string, string>> newDict = newMods.ToDictionary(m => m["id"]);

        List<string> allIds = oldDict.Keys.Union(newDict.Keys).ToList();

        Log.Debug($"Found {allIds.Count} mods to compare");

        List<ModEntry> result = allIds.Select(id =>
        {
            Dictionary<string, string>? oldMod = oldDict.GetValueOrDefault(id);
            Dictionary<string, string>? newMod = newDict.GetValueOrDefault(id);
            string name = newMod?["name"] ?? oldMod?["name"] ?? id;

            return new ModEntry(name, newMod?.GetValueOrDefault("version") ?? string.Empty, oldMod?.GetValueOrDefault("version") ?? string.Empty);
        }).ToList();

        Log.Debug($"Finished generating mods differences with {result.Count} entries");
        return result;
    }

    internal static void AppendModsSection(StringBuilder sb, List<ModEntry> modsDiff)
    {
        Log.Debug("Appending mods section to changelog");

        List<ModEntry> added = modsDiff.Where(e => string.IsNullOrEmpty(e.OldVersion)).ToList();
        List<ModEntry> removed = modsDiff.Where(e => string.IsNullOrEmpty(e.NewVersion)).ToList();
        List<ModEntry> changed = modsDiff.Where(e => !string.IsNullOrEmpty(e.OldVersion) && !string.IsNullOrEmpty(e.NewVersion) && e.OldVersion != e.NewVersion).ToList();

        int addedCount = added.Count;
        int removedCount = removed.Count;
        int changedCount = changed.Count;

        Log.Debug($"{addedCount} Mods added");
        Log.Debug($"{removedCount} Mods removed");
        Log.Debug($"{changedCount} Mods changed");

        sb.AppendLine("## 🛠️ Mods\n");

        if (addedCount == 0 && removedCount == 0 && changedCount == 0)
        {
            sb.AppendLine("No changes to mods\n");
            sb.AppendLine("---\n");
            return;
        }

        if (addedCount > 0)
        {
            sb.AppendLine($"<details open>\n<summary>Added ({added.Count})</summary>");
            added.ForEach(mod => sb.AppendLine($"- {mod.Name}  ({mod.NewVersion})"));
            sb.AppendLine("</details>\n");
        }

        if (removedCount > 0)
        {
            sb.AppendLine($"<details>\n<summary>Removed ({removed.Count})</summary>");
            removed.ForEach(mod => sb.AppendLine($"- {mod.Name}  ({mod.OldVersion})"));
            sb.AppendLine("</details>\n");
        }

        if (changedCount > 0)
        {
            sb.AppendLine($"<details>\n<summary>Changed ({changed.Count})</summary>");
            changed.ForEach(mod => sb.AppendLine($"- {mod.Name}  ({mod.OldVersion} -> {mod.NewVersion})"));
            sb.AppendLine("</details>\n");
        }

        sb.AppendLine("---\n");
    }

    internal static async Task<List<FileEntry>> GetFolderDiffAsync(string oldExport, string newExport, Dictionary<string, string> oldFiles, Dictionary<string, string> newFiles,
        CancellationToken ct = default)
    {
        Log.Debug("Generating folder differences");
        HashSet<string> allKeys = new(oldFiles.Keys);
        allKeys.UnionWith(newFiles.Keys);
        allKeys.RemoveWhere(k => !k.Contains(Path.DirectorySeparatorChar));

        Stopwatch stopwatch = Stopwatch.StartNew();
        Log.Information($"Comparing {allKeys.Count} files");

        ConcurrentBag<FileEntry> diffs = [];
        int added = 0, removed = 0, changed = 0;

        ExecutionDataflowBlockOptions executionOptions = new()
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        TransformBlock<string, FileEntry> processBlock = new(async key =>
        {
            bool oldExists = oldFiles.TryGetValue(key, out string? oldPath);
            bool newExists = newFiles.TryGetValue(key, out string? newPath);

            if (oldExists && newExists)
            {
                string oldFile = Path.Combine(oldExport, oldPath!);
                string newFile = Path.Combine(newExport, newPath!);
                if (!await JsonFilesEqualAsync(oldFile, newFile, ct))
                {
                    Interlocked.Increment(ref changed);
                    return new FileEntry(key, FileChangedType.Changed);
                }
            }
            else if (oldExists)
            {
                Interlocked.Increment(ref removed);
                return new FileEntry(key, FileChangedType.Removed);
            }
            else // new file exists only
            {
                Interlocked.Increment(ref added);
                return new FileEntry(key, FileChangedType.Added);
            }

            return null;
        }, executionOptions);

        // Block to collect non-null FileEntry results.
        ActionBlock<FileEntry> collectBlock = new(entry =>
        {
            if (entry != null)
                diffs.Add(entry);
        }, executionOptions);

        // Link the pipeline.
        processBlock.LinkTo(collectBlock, new DataflowLinkOptions { PropagateCompletion = true });

        // Feed all keys into the processing block.
        foreach (string key in allKeys) await processBlock.SendAsync(key, ct);
        processBlock.Complete();
        await collectBlock.Completion;

        Log.Information($"Found {added} added, {changed} changed, {removed} removed files in {stopwatch.ElapsedMilliseconds}ms");
        return diffs.ToList();
    }

    internal static async Task<bool> JsonFilesEqualAsync(string file1, string file2, CancellationToken ct = default)
    {
        string json1 = await File.ReadAllTextAsync(file1, ct);
        string json2 = await File.ReadAllTextAsync(file2, ct);

        JToken token1 = JToken.Parse(json1);
        JToken token2 = JToken.Parse(json2);

        return JTokenEquals(token1, token2);
    }

    internal static bool JTokenEquals(JToken t1, JToken t2)
    {
        if (t1.Type != t2.Type)
            return false;

        return t1 switch
        {
            JObject o1 when t2 is JObject o2 => o1.Count == o2.Count && o1.Properties().All(p => o2.TryGetValue(p.Name, out JToken? v) && JTokenEquals(p.Value, v)),
            JArray a1 when t2 is JArray a2 => a1.Count == a2.Count && a1.All(item => a2.Any(i => JTokenEquals(item, i))),
            _ => JToken.DeepEquals(t1, t2)
        };
    }

    internal static Dictionary<SectionType, List<FileEntry>> GroupBySection(List<FileEntry> fileDiffs)
    {
        ConcurrentDictionary<SectionType, ConcurrentBag<FileEntry>> sectionMap = new();

        Parallel.ForEach(fileDiffs, entry =>
        {
            (string trimmedPath, SectionType section) = GetSectionType(entry.RelativePath);
            sectionMap.GetOrAdd(section, _ => []).Add(entry with { PrettyPath = trimmedPath });
        });

        return sectionMap.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
    }

    internal static (string TrimmedPath, SectionType Type) GetSectionType(string path)
    {
        string[] parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].ToLowerInvariant();
            if (part.Contains("recipe") || part.Contains("recipes"))
                return (string.Join(Path.DirectorySeparatorChar, parts.Skip(i + 1)), SectionType.Recipes);
            if (part.Contains("tag") || part.Contains("tags"))
                return (string.Join(Path.DirectorySeparatorChar, parts.Skip(i + 1)), SectionType.Tags);
            if (part.Contains("registry") || part.Contains("registries"))
                return (string.Join(Path.DirectorySeparatorChar, parts.Skip(i + 1)), SectionType.Registries);
            if (part.Contains("loot_table") || part.Contains("loot_tables"))
                return (string.Join(Path.DirectorySeparatorChar, parts.Skip(i + 1)), SectionType.LootTable);
        }

        return (path, SectionType.Unknown);
    }

    internal static string PrintSectionHeader(SectionType section)
    {
        return section switch
        {
            SectionType.Recipes => "🍳 Recipes",
            SectionType.Tags => "🏷️ Tags",
            SectionType.Registries => "✍️ Registries",
            SectionType.LootTable => "🗝️ Loot Tables",
            _ => "❓ Unknown"
        };
    }

    internal static string BuildEntry(string oldExport, string newExport, FileEntry entry, SectionType section)
    {
        string path = string.Join(Path.DirectorySeparatorChar.ToString(), entry.RelativePath.Split(Path.DirectorySeparatorChar).Skip(1));
        StringBuilder sb = new();
        sb.AppendLine("<details>");
        sb.AppendLine($"<summary>{(section == SectionType.Unknown ? entry.PrettyPath ?? entry.RelativePath : path)}</summary>\n");
        sb.AppendLine("```diff");

        string oldPath = Path.Combine(oldExport, entry.RelativePath);
        string newPath = Path.Combine(newExport, entry.RelativePath);

        string oldText = File.Exists(oldPath) ? File.ReadAllText(oldPath) : string.Empty;
        string newText = File.Exists(newPath) ? File.ReadAllText(newPath) : string.Empty;

        bool summarizeSkips = false;

        if (section == SectionType.Registries)
        {
            summarizeSkips = true;
            oldText = StripJsonObjectValues(oldText);
            newText = StripJsonObjectValues(newText);
        }
        else
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(newText.Length > 0 ? newText : oldText);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    summarizeSkips = true;
            }
            catch
            {
                /* Ignore parsing issues */
            }
        }

        DiffPaneModel? diff = InlineDiffBuilder.Diff(oldText, newText);

        if (summarizeSkips)
        {
            bool array = newText.TrimStart().StartsWith('[');

            sb.AppendLine(array ? "[" : "{");

            int skipped = 0;

            foreach (DiffPiece? line in diff.Lines)
            {
                if (diff.Lines.Last().Equals(line))
                {
                    sb.AppendLine($"\t...( {skipped} entries)");
                    break;
                }

                if (line.Type == ChangeType.Unchanged)
                {
                    skipped++;
                    continue;
                }

                if (skipped > 0)
                {
                    sb.AppendLine($"\t...( {skipped} entries)");
                    skipped = 0;
                }

                sb.AppendLine(line.Type switch
                {
                    ChangeType.Inserted => "+ " + line.Text,
                    ChangeType.Deleted => "- " + line.Text,
                    _ => "  " + line.Text
                });
            }

            sb.AppendLine(array ? "]" : "}");
        }
        else
        {
            foreach (DiffPiece? line in diff.Lines)
                sb.AppendLine(line.Type switch
                {
                    ChangeType.Inserted => "+ " + line.Text,
                    ChangeType.Deleted => "- " + line.Text,
                    _ => "  " + line.Text
                });
        }

        sb.AppendLine("```");
        sb.AppendLine("</details>\n");
        return sb.ToString();
    }

    internal static string StripJsonObjectValues(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return json;

            List<string> keys = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToList();

            return string.Join('\n', keys);
        }
        catch
        {
            return json;
        }
    }
}