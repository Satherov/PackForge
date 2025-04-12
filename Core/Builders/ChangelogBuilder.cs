using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using PackForge.Core.Util;
using Serilog;
using Serilog.Events;
using JsonSerializer = System.Text.Json.JsonSerializer;

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
    private static readonly string ChangelogPath = Path.Combine(App.AppDataPath, "data", "changelog");
    private static readonly string OldExportDir = Path.Combine(ChangelogPath, "export-old");
    private static readonly string NewExportDir = Path.Combine(ChangelogPath, "export-new");

    public static async Task GenerateFullChangelogAsync(string changelogPath, string exportSourcePath, string version, CancellationToken ct = default)
    {
        if (Validator.DirectoryEmpty(exportSourcePath)) return;

        if (!Validator.DirectoryExists(OldExportDir))
        {
            Log.Warning("Old export directory does not exist, if this is your first time running the generator, do another kubejs export and then run it again");
            await FileHelper.CopyFilesAsync(exportSourcePath, OldExportDir, null, ct);
            return;
        }

        if (Validator.DirectoryExists(NewExportDir, LogEventLevel.Debug)) Directory.Delete(NewExportDir, true);

        Log.Information("Collecting new export files");
        await FileHelper.CopyFilesAsync(exportSourcePath, NewExportDir, null, ct);

        Stopwatch stopwatch = Stopwatch.StartNew();
        Log.Information("Starting changelog generation");

        StringBuilder sb = new();

        sb.AppendLine("# Changelog\n");
        sb.AppendLine($"# 🎞️ {version}\n");
        sb.AppendLine("## 📰 General changes and notes\n");
        sb.AppendLine("Summary of changes here!\n");
        sb.AppendLine("---\n");

        List<ModEntry> modsDiff = await GetModsDiffAsync(ct);
        AppendModsSection(sb, modsDiff);

        Log.Debug("Collecting files");
        Task<Dictionary<string, string>> oldTask =
            Task.Run(
                () => Directory.EnumerateFiles(OldExportDir, "*.json", SearchOption.AllDirectories)
                    .Where(f => Path.GetRelativePath(OldExportDir, f).Contains(Path.DirectorySeparatorChar)).ToDictionary(f => Path.GetRelativePath(OldExportDir, f), f => f), ct);

        Task<Dictionary<string, string>> newTask =
            Task.Run(
                () => Directory.EnumerateFiles(NewExportDir, "*.json", SearchOption.AllDirectories)
                    .Where(f => Path.GetRelativePath(NewExportDir, f).Contains(Path.DirectorySeparatorChar)).ToDictionary(f => Path.GetRelativePath(NewExportDir, f), f => f), ct);

        await Task.WhenAll(oldTask, newTask);

        List<FileEntry> fileDiffs = await GetFolderDiffAsync(oldTask.Result, newTask.Result, ct);
        Dictionary<SectionType, List<FileEntry>> sectionMap = GroupBySection(fileDiffs);

        Log.Information($"Writing changelog entries to file");
        foreach ((SectionType section, List<FileEntry> entries) in sectionMap.OrderBy(x => x.Key))
        {
            sb.AppendLine($"## {PrintSectionHeader(section)}\n");

            foreach (FileChangedType changeType in Enum.GetValues<FileChangedType>())
            {
                List<FileEntry> group = entries.Where(e => e.FileChangedType == changeType).OrderBy(e => e.RelativePath).ToList();
                if (group.Count == 0) continue;

                sb.AppendLine("<details>");
                sb.AppendLine($"<summary>{changeType} ({group.Count})</summary>");
                sb.AppendLine("<blockquote>\n");

                foreach (string entryText in group.Select(entry => BuildEntry(entry, section)).Where(entryText => !string.IsNullOrEmpty(entryText))) sb.AppendLine(entryText);

                sb.AppendLine("</blockquote>");
                sb.AppendLine("</details>\n");
            }
        }

        await File.WriteAllTextAsync(Path.Combine(changelogPath, $"CHANGELOG-{version}.md"), sb.ToString(), ct);
        Log.Information($"Changelog generated successfully after {stopwatch.ElapsedMilliseconds}ms");

        Directory.Delete(OldExportDir, true);
        Directory.Move(NewExportDir, OldExportDir);
    }

    private static async Task<List<ModEntry>> GetModsDiffAsync(CancellationToken ct = default)
    {
        string oldModsPath = Path.Combine(OldExportDir, "mods.json");
        string newModsPath = Path.Combine(NewExportDir, "mods.json");

        if (!Validator.FileExists(oldModsPath) || !Validator.FileExists(newModsPath)) return [];

        List<Dictionary<string, string>> oldMods = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(await File.ReadAllTextAsync(oldModsPath, ct)) ?? [];
        List<Dictionary<string, string>> newMods = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(await File.ReadAllTextAsync(newModsPath, ct)) ?? [];

        Dictionary<string, Dictionary<string, string>> oldDict = oldMods.ToDictionary(m => m["id"]);
        Dictionary<string, Dictionary<string, string>> newDict = newMods.ToDictionary(m => m["id"]);

        IEnumerable<string> allIds = oldDict.Keys.Union(newDict.Keys);

        return allIds.Select(id =>
        {
            Dictionary<string, string>? oldMod = oldDict.GetValueOrDefault(id);
            Dictionary<string, string>? newMod = newDict.GetValueOrDefault(id);
            string name = newMod?["name"] ?? oldMod?["name"] ?? id;

            return new ModEntry(name, newMod?.GetValueOrDefault("version") ?? string.Empty, oldMod?.GetValueOrDefault("version") ?? string.Empty);
        }).ToList();
    }

    private static void AppendModsSection(StringBuilder sb, List<ModEntry> modsDiff)
    {
        List<ModEntry> added = modsDiff.Where(e => string.IsNullOrEmpty(e.OldVersion)).ToList();
        List<ModEntry> removed = modsDiff.Where(e => string.IsNullOrEmpty(e.NewVersion)).ToList();
        List<ModEntry> changed = modsDiff.Where(e => !string.IsNullOrEmpty(e.OldVersion) && !string.IsNullOrEmpty(e.NewVersion) && e.OldVersion != e.NewVersion).ToList();

        sb.AppendLine("## 🛠️ Mods\n");
        sb.AppendLine($"<details open>\n<summary>Added ({added.Count})</summary>");
        added.ForEach(mod => sb.AppendLine($"- {mod.Name}  ({mod.NewVersion})"));
        sb.AppendLine("</details>\n");

        sb.AppendLine($"<details open>\n<summary>Removed ({removed.Count})</summary>");
        removed.ForEach(mod => sb.AppendLine($"- {mod.Name}  ({mod.OldVersion})"));
        sb.AppendLine("</details>\n");

        sb.AppendLine($"<details>\n<summary>Changed ({changed.Count})</summary>");
        changed.ForEach(mod => sb.AppendLine($"- {mod.Name}  ({mod.OldVersion} -> {mod.NewVersion})"));
        sb.AppendLine("</details>\n");
    }

    private static async Task<List<FileEntry>> GetFolderDiffAsync(Dictionary<string, string> oldFiles, Dictionary<string, string> newFiles, CancellationToken ct = default)
    {
        HashSet<string> allKeys = new(oldFiles.Keys);
        allKeys.UnionWith(newFiles.Keys);
        allKeys.RemoveWhere(k => !k.Contains(Path.DirectorySeparatorChar));

        Stopwatch stopwatch = Stopwatch.StartNew();
        Log.Information($"Comparing {allKeys.Count} files");

        ConcurrentBag<FileEntry> diffs = [];
        int added = 0;
        int removed = 0;
        int changed = 0;

        await Parallel.ForEachAsync(allKeys, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct }, async (key, token) =>
        {
            bool oldExists = oldFiles.TryGetValue(key, out string? oldPath);
            bool newExists = newFiles.TryGetValue(key, out string? newPath);

            switch (oldExists)
            {
                case true when newExists:
                {
                    string oldFile = Path.Combine(OldExportDir, oldPath!);
                    string newFile = Path.Combine(NewExportDir, newPath!);

                    if (!await FilesEqualAsync(oldFile, newFile, token))
                    {
                        diffs.Add(new FileEntry(key, FileChangedType.Changed));
                        Interlocked.Increment(ref changed);
                    }
                }
                    break;
                case true:
                {
                    diffs.Add(new FileEntry(key, FileChangedType.Removed));
                    Interlocked.Increment(ref removed);
                }
                    break;
                default:
                {
                    diffs.Add(new FileEntry(key, FileChangedType.Added));
                    Interlocked.Increment(ref added);
                }
                    break;
            }
        });

        Log.Information($"Found {added} added, {changed} changed, {removed} removed files in {stopwatch.ElapsedMilliseconds}ms");
        return diffs.ToList();
    }

    private static async Task<bool> FilesEqualAsync(string file1, string file2, CancellationToken ct = default)
    {
        const int bufferSize = 8192;
        await using FileStream fs1 = File.OpenRead(file1);
        await using FileStream fs2 = File.OpenRead(file2);

        if (fs1.Length != fs2.Length)
            return false;

        byte[] buffer1 = ArrayPool<byte>.Shared.Rent(bufferSize);
        byte[] buffer2 = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            int bytesRead;
            do
            {
                bytesRead = await fs1.ReadAsync(buffer1.AsMemory(0, bufferSize), ct);
                int bytesRead2 = await fs2.ReadAsync(buffer2.AsMemory(0, bufferSize), ct);
                if (bytesRead != bytesRead2 || !buffer1.AsSpan(0, bytesRead).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
                    return false;
            } while (bytesRead > 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer1);
            ArrayPool<byte>.Shared.Return(buffer2);
        }

        return true;
    }


    private static Dictionary<SectionType, List<FileEntry>> GroupBySection(List<FileEntry> fileDiffs)
    {
        ConcurrentDictionary<SectionType, ConcurrentBag<FileEntry>> sectionMap = new();

        Parallel.ForEach(fileDiffs, entry =>
        {
            (string trimmedPath, SectionType section) = GetSectionType(entry.RelativePath);
            sectionMap.GetOrAdd(section, _ => []).Add(entry with { PrettyPath = trimmedPath });
        });

        return sectionMap.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
    }

    private static (string TrimmedPath, SectionType Type) GetSectionType(string path)
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

    private static string PrintSectionHeader(SectionType section)
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

    private static string BuildEntry(FileEntry entry, SectionType section)
    {
        string path = string.Join(Path.DirectorySeparatorChar.ToString(), entry.RelativePath.Split(Path.DirectorySeparatorChar).Skip(1));
        StringBuilder sb = new();
        sb.AppendLine("<details>");
        sb.AppendLine($"<summary>{(section == SectionType.Unknown ? entry.PrettyPath ?? entry.RelativePath : path)}</summary>\n");
        sb.AppendLine("```diff");

        string oldPath = Path.Combine(OldExportDir, entry.RelativePath);
        string newPath = Path.Combine(NewExportDir, entry.RelativePath);

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

        DiffPaneModel? diff = new InlineDiffBuilder(new Differ()).BuildDiffModel(oldText, newText);
        if (!diff.Lines.Any(line => line.Type is ChangeType.Inserted or ChangeType.Deleted))
            return string.Empty;

        if (summarizeSkips)
        {
            sb.AppendLine(newText.TrimStart().StartsWith('[') ? "[" : "{");

            int skipped = 0;
            foreach (DiffPiece? line in diff.Lines)
            {
                if (line.Type == ChangeType.Unchanged)
                {
                    skipped++;
                    continue;
                }

                if (skipped > 0)
                {
                    sb.AppendLine($"    ...( {skipped} entries)");
                    skipped = 0;
                }

                string prefix = line.Type switch
                {
                    ChangeType.Inserted => "+",
                    ChangeType.Deleted => "-",
                    _ => "!"
                };

                sb.AppendLine($"{prefix} {line.Text}");
            }

            if (skipped > 0)
                sb.AppendLine($"    ...( {skipped} entries)");

            sb.AppendLine(newText.TrimStart().StartsWith('[') ? "]" : "}");
        }
        else
        {
            foreach (DiffPiece? line in diff.Lines)
                sb.AppendLine(line.Type switch
                {
                    ChangeType.Inserted => "+ " + line.Text,
                    ChangeType.Deleted => "- " + line.Text,
                    ChangeType.Unchanged => "  " + line.Text,
                    _ => "! " + line.Text
                });
        }

        sb.AppendLine("```");
        sb.AppendLine("</details>\n");
        return sb.ToString();
    }

    private static string StripJsonObjectValues(string json)
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