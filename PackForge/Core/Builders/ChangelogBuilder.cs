using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PackForge.Core.Helpers;
using Serilog;

namespace PackForge.Core.Builders;
public static class ChangelogBuilder
{
    private static readonly string ChangelogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "PackForge", "data", "changelog");
    
    private static readonly string OldExportPath = Path.Combine(
        ChangelogPath, "export-old");
    
    private static readonly string NewExportPath = Path.Combine(
        ChangelogPath, "export-new");

    static ChangelogBuilder()
    {
        if (!Validator.DirectoryExists(ChangelogPath, logLevel:"debug")) Directory.CreateDirectory(ChangelogPath);
        if (!Validator.DirectoryExists(OldExportPath, logLevel:"debug")) Directory.CreateDirectory(OldExportPath);
        if (!Validator.DirectoryExists(NewExportPath, logLevel:"debug")) Directory.CreateDirectory(NewExportPath);
    }

    /// <summary>
    /// Copies the new export from the given path to the app dir
    /// </summary>
    /// <param name="folderPath">Folder poth of the new export</param>
    /// <param name="ct">Cancellation Token used when the User kills the task</param>
    /// <returns></returns>
    private static async Task<string> GetNewExport (string folderPath, CancellationToken ct) {
        Log.Debug("Collecting files for new export");
        if(Validator.DirectoryEmpty(folderPath)) return string.Empty;
        await FileHelper.CopyMatchingFilesAsync(OldExportPath, NewExportPath, null, ct);
        Log.Debug("New export collected");
        return NewExportPath;
    }
    
    /// <summary>
    /// Generates the Changelog for the given folder
    /// </summary>
    /// <param name="folderPath">The path to check for the new export</param>
    /// <param name="outputDirectory">Where to output the Changelog to</param>
    /// <param name="ct">Cancellation Token used when the User kills the task</param>
    public static async Task GenerateChangelogAsync(string folderPath, string outputDirectory, string version, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        Log.Information("Generating changelog");
        //var newExport = await GetNewExport(OldExportPath, ct);
        //if(Validator.DirectoryEmpty(newExport)) return;

        var diffResult = await Task.Run(() => CompareFolders(OldExportPath, NewExportPath), ct);
        var modsDiffResult = await CompareModsJsonAsync(OldExportPath, NewExportPath, ct);
  
        await WriteFullChangelog(diffResult, modsDiffResult, Path.Combine(outputDirectory, $"CHANGELOG-{version}.md"), ct);
        
        //Log.Debug("Cleaning up");
        // _ = FileHelper.CopyMatchingFilesAsync(newExport, OldExportPath, null, ct);
        //Log.Debug("Moved new export to old export");
        //Directory.Delete(newExport, true);
        
        stopwatch.Stop();
        Log.Information($"Changelog generation took a total of {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Compares two given folders and returns the differences consisting "Added", "Changed" and "Removed".
    /// </summary>
    /// <param name="oldFolder">The original folder</param>
    /// <param name="newFolder">The new Folder</param>
    /// <returns>Difference between the folders</returns>
    private static FolderDiffResult CompareFolders(string oldFolder, string newFolder)
    {
        var stopwatch = Stopwatch.StartNew();
        Log.Information("Comparing exports");
        
        if (Validator.IsNullOrWhiteSpace(oldFolder) || Validator.IsNullOrWhiteSpace(newFolder)) 
            return FolderDiffResult.Empty;
        
        var oldExport = Directory.GetFiles(oldFolder, "*", SearchOption.AllDirectories)
            .Select(fullPath => 
            {
                var rel = Path.GetRelativePath(oldFolder, fullPath);
                return (Rel: rel, Full: fullPath);
            })
            .Where(pair => pair.Rel.Contains(Path.DirectorySeparatorChar) || pair.Rel.Contains(Path.AltDirectorySeparatorChar))
            .ToDictionary(pair => pair.Rel, pair => pair.Full);
        
        var newExport = Directory.GetFiles(newFolder, "*", SearchOption.AllDirectories)
            .Select(fullPath =>
            {
                var rel = Path.GetRelativePath(newFolder, fullPath);
                return (Rel: rel, Full: fullPath);
            })
            .Where(pair => pair.Rel.Contains(Path.DirectorySeparatorChar) || pair.Rel.Contains(Path.AltDirectorySeparatorChar))
            .ToDictionary(pair => pair.Rel, pair => pair.Full);
    
        var result = new FolderDiffResult();
        var lockObj = new object();
    
        Parallel.Invoke(
            () => Parallel.ForEach(oldExport, (kvp, _) =>
            {
                if (!newExport.TryGetValue(kvp.Key, out var value))
                {
                    lock (lockObj) result.DeletedFiles.Add(kvp.Key);
                }
                else if (!FilesAreEqual(kvp.Value, value))
                {
                    lock (lockObj) result.ChangedFiles.Add(kvp.Key);
                }
            }),
            () => Parallel.ForEach(newExport.Where(kvp => !oldExport.ContainsKey(kvp.Key)), (kvp, _) =>
            {
                lock (lockObj) result.AddedFiles.Add(kvp.Key);
            })
        );
    
        Log.Information($"Added files: {result.AddedFiles.Count}");
        Log.Information($"Changed files: {result.ChangedFiles.Count}");
        Log.Information($"Deleted files: {result.DeletedFiles.Count}");
        
        stopwatch.Stop();
        Log.Information($"Comparison took {stopwatch.ElapsedMilliseconds}ms");
    
        return result;
    }

    /// <summary>
    /// Compares two exports and returns the differences in mods.
    /// </summary>
    /// <param name="oldExport">The original Export</param>
    /// <param name="newExport">The new export</param>
    /// <param name="ct">Cancellation Token used when the User kills the task</param>
    /// <returns>Difference between them</returns>
    private static async Task<ModsDiffResult> CompareModsJsonAsync(string oldExport, string newExport, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        Log.Information("Comparing mods files");
        
        var oldModsPath = Path.Combine(oldExport, "mods.json");
        var newModsPath = Path.Combine(newExport, "mods.json");
        
        if (!File.Exists(oldModsPath) || !File.Exists(newModsPath))
            return ModsDiffResult.Empty;

        var oldContent = await File.ReadAllTextAsync(oldModsPath, ct);
        var newContent = await File.ReadAllTextAsync(newModsPath, ct);

        var oldMods = JsonSerializer.Deserialize<List<ModEntry>>(oldContent) ?? [];
        var newMods = JsonSerializer.Deserialize<List<ModEntry>>(newContent) ?? [];

        var oldDict = oldMods.ToDictionary(m => m.Id, m => m);
        var newDict = newMods.ToDictionary(m => m.Id, m => m);

        var result = new ModsDiffResult();
        
        var lockObj = new object();
        Parallel.Invoke(
            () => Parallel.ForEach(newDict.Where(kv => !oldDict.ContainsKey(kv.Key)), kv =>
            {
                lock (lockObj)
                {
                    result.AddedMods.Add((kv.Value.Name, kv.Value.Version));
                }
            }),
            () => Parallel.ForEach(oldDict.Where(kv => !newDict.ContainsKey(kv.Key)), kv =>
            {
                lock (lockObj)
                {
                    result.RemovedMods.Add((kv.Value.Name, kv.Value.Version));
                }
            }),
            () => Parallel.ForEach(newDict, kv =>
            {
                if (!oldDict.TryGetValue(kv.Key, out var oldMod) || oldMod.Version == kv.Value.Version) return;
                
                lock (lockObj)
                {
                    result.ChangedMods.Add((kv.Value.Name, oldMod.Version, kv.Value.Version));
                }
            })
        );
        
        Log.Information($"Added mods: {result.AddedMods.Count}");
        Log.Information($"Changed mods: {result.ChangedMods.Count}");
        Log.Information($"Removed mods: {result.RemovedMods.Count}");

        stopwatch.Stop();
        Log.Information($"Mod comparison took {stopwatch.ElapsedMilliseconds}ms");
        
        return result;
    }

    /// <summary>
    /// Writes the differences in mods to the changelog
    /// </summary>
    /// <param name="modsDiff">Result of a previous comparison</param>
    /// <param name="ct">Cancellation Token to use when the User kills the task</param>
    /// <returns></returns>
    private static async Task<string> WriteModsDiffSectionAsync(ModsDiffResult modsDiff, CancellationToken ct)
    {
        if (Validator.IsNullOrWhiteSpace(modsDiff, logLevel: "debug"))
            return string.Empty;

        var addedTask = Task.Run(() =>
        {
            var sb = new StringBuilder();
            if (modsDiff.AddedMods.Count == 0) return sb.ToString();
            sb.AppendLine($"### Added Mods ({modsDiff.AddedMods.Count})");
            sb.AppendLine();
            foreach (var (name, version) in modsDiff.AddedMods) sb.AppendLine($"- {name} ({version})");
            sb.AppendLine();
            return sb.ToString();
        }, ct);

        var removedTask = Task.Run(() =>
        {
            var sb = new StringBuilder();
            if (modsDiff.RemovedMods.Count == 0) return sb.ToString();
            sb.AppendLine($"### Removed Mods ({modsDiff.RemovedMods.Count})");
            sb.AppendLine();
            foreach (var (name, version) in modsDiff.RemovedMods) sb.AppendLine($"- {name} ({version})");
            sb.AppendLine();
            return sb.ToString();
        }, ct);

        var changedTask = Task.Run(() =>
        {
            var sb = new StringBuilder();
            if (modsDiff.ChangedMods.Count == 0) return sb.ToString();
            sb.AppendLine($"### Changed Mods ({modsDiff.ChangedMods.Count})");
            sb.AppendLine();
            foreach (var (name, oldVersion, newVersion) in modsDiff.ChangedMods) sb.AppendLine($"- {name} ({oldVersion}) -> ({newVersion})");
            sb.AppendLine();
            return sb.ToString();
        }, ct);

        await Task.WhenAll(addedTask, removedTask, changedTask);

        var finalSb = new StringBuilder();
        finalSb.AppendLine("## Mods");
        finalSb.AppendLine();
        finalSb.Append(addedTask.Result);
        finalSb.Append(removedTask.Result);
        finalSb.Append(changedTask.Result);

        return finalSb.ToString();
    }

    /// <summary>
    /// Compares two given files and returns if they are equal. For jsons use the content, for other files use the hash.
    /// </summary>
    /// <param name="file1">First file</param>
    /// <param name="file2">Second file</param>
    /// <returns>Are the equal?</returns>
    private static bool FilesAreEqual(string file1, string file2)
    {
        if (file1.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && file2.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonDiffHelper.CompareJsons(file1, file2);
        }
        
        return ComputeFileHash(file1) == ComputeFileHash(file2);
        
        static string ComputeFileHash(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return Convert.ToHexStringLower(hash);
        }
    }

    /// <summary>
    /// Processes a single section of the changelog.
    /// </summary>
    /// <param name="sectionName">Name of the section being processed</param>
    /// <param name="entries">List of all the entires</param>
    /// <param name="prefix">Prefix to add before each line</param>
    /// <param name="isRegistries">Special behaviour for the registries section</param>
    /// <param name="ct">Cancellation Token to use if the user kills the task</param>
    /// <returns></returns>
    private static async Task<string> WriteDiffSectionAsync(string sectionName, List<FolderEntry> entries, char? prefix, bool isRegistries, CancellationToken ct)
    {
        if (Validator.IsNullOrWhiteSpace(sectionName) || entries.Count == 0)
            return string.Empty;

        Log.Debug($"Processing {sectionName} section ({entries.Count})");

        var sb = new StringBuilder();
        sb.AppendLine($"### {sectionName} ({entries.Count})");
        sb.AppendLine();
        
        var isChangedSection = sectionName.Equals("Changed", StringComparison.OrdinalIgnoreCase);
        var oldExportPath = OldExportPath;
        var newExportPath = NewExportPath;
        
        var results = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(entries, ct, async (entry, token) =>
        {
            var result = await ProcessEntryAsync(entry, prefix, isRegistries, isChangedSection, oldExportPath, newExportPath, token);
            if (!string.IsNullOrEmpty(result))
            {
                results.Add(result);
            }
        });
        
        foreach (var res in results)
        {
            sb.Append(res);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Processes a single entry and writes it to the changelog
    /// </summary>
    /// <param name="entry">Entry to process</param>
    /// <param name="prefix">Prefix to add before each line</param>
    /// <param name="isRegistries">Special behaviour for the registries section</param>
    /// <param name="isChangedSection">Special behaviour for the changed section</param>
    /// <param name="oldExportPath">Path to the file in the old export</param>
    /// <param name="newExportPath">Path to the file in the new export</param>
    /// <param name="token">Cancellation Token to use if the user kills the task</param>
    /// <returns>Entry to be added to the changelog</returns>
    private static async Task<string> ProcessEntryAsync(
        FolderEntry entry,
        char? prefix,
        bool isRegistries,
        bool isChangedSection,
        string oldExportPath,
        string newExportPath,
        CancellationToken token)
    {
        // We love cache
        var filePath = entry.FilePath;
        var isJson = filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        var slashIndex = filePath.IndexOf('\\');
        var displayPath = filePath;
        if (slashIndex >= 0) displayPath = displayPath[(slashIndex + 1)..];
        List<string> linesToPrint;

        var entrySb = new StringBuilder();
        entrySb.AppendLine("<details>");
        entrySb.AppendLine($"<summary>{displayPath}</summary>");
        entrySb.AppendLine();
        entrySb.AppendLine("```diff");

        if (isChangedSection && isJson)
        {
            var oldFilePath = Path.Combine(oldExportPath, filePath);
            var newFilePath = Path.Combine(newExportPath, filePath);
            linesToPrint = await JsonDiffHelper.GenerateDiffAsync(oldFilePath, newFilePath);
        }
        else
        {
            var rawLines = entry.FileContent;
            linesToPrint = prefix.HasValue
                ? rawLines.Select(line => $"{prefix}{line}").ToList()
                : rawLines.ToList();
        }

        if (isChangedSection)
        {
            linesToPrint = JsonDiffHelper.CombinePairs(linesToPrint);
        }

        List<string> finalLines;

        if (isRegistries || await ShouldCollapse())
        {
            var collapsedLines = new List<string>(linesToPrint.Count);
            var inCollapse = false;
            var collapseCount = 0;

            foreach (var line in linesToPrint.Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                switch (line[0])
                {
                    case '{':
                    case '[':
                        collapsedLines.Add(line);
                        continue;
                }

                if ((line[0] == '-' || line[0] == '+'))
                {
                    if (inCollapse && collapseCount > 0)
                    {
                        collapsedLines.Add($"  ... ({collapseCount} entries)");
                        collapseCount = 0;
                        inCollapse = false;
                    }

                    collapsedLines.Add(line);
                }
                else
                {
                    inCollapse = true;
                    collapseCount++;
                }
            }

            if (inCollapse && collapseCount > 0)
            { 
                collapsedLines.Add($"  ... ({collapseCount} entries)");
                
                switch (collapsedLines.First()[0])
                {
                    case '{': 
                        collapsedLines.Add("}");
                        break;
                    case '[': 
                        collapsedLines.Add("]");
                        break;
                }
            }

            finalLines = collapsedLines;
        }
        else
        {
            finalLines = linesToPrint;
        }
        
        if (isRegistries)
        {
            for (var i = 0; i < finalLines.Count; i++)
            {
                var orig = finalLines[i];
                var diffPrefix = orig[0];
                var trimmed = orig.TrimStart('-', '+', ' ');
                if (!trimmed.StartsWith($"\"") || trimmed.IndexOf("\":", StringComparison.Ordinal) < 0)
                    continue;
                var colonIndex = trimmed.IndexOf("\":", StringComparison.Ordinal);
                var keyPart = trimmed[..(colonIndex + 1)];
                finalLines[i] = $"{diffPrefix} {keyPart}";
            }
        }
        
        if (OnlyCollapsed(finalLines)) return string.Empty;
        
        foreach (var line in finalLines)
        {
            entrySb.AppendLine(line);
        }

        entrySb.AppendLine("```");
        entrySb.AppendLine();
        entrySb.AppendLine("</details>");
        entrySb.AppendLine();

        return entrySb.ToString();

        async Task<bool> ShouldCollapse()
        {
            var shouldCollapse = false;
            if (!isJson) return shouldCollapse;
            var newFilePath = Path.Combine(newExportPath, filePath);
            if (!File.Exists(newFilePath)) return shouldCollapse;
            var content = (await File.ReadAllTextAsync(newFilePath, token)).TrimStart();
            shouldCollapse = content.Length > 0 && (content[0] == '[' || isRegistries);
            return shouldCollapse;
        }

        static bool OnlyCollapsed(List<string> lines)
        {
            var onlyCollapsed = true;
            
            foreach (var line in lines)
            {
                if (line.Length > 0 && (line[0] == '+' || line[0] == '-'))
                {
                    onlyCollapsed = false;
                    break;
                }

                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("... (")) continue;
                onlyCollapsed = false;
                break;
            }
            
            return onlyCollapsed;
        }
    }

    /// <summary>
    /// Converts the given FolderDiffResult to a list of FolderEntries
    /// </summary>
    /// <param name="folderDiff">Results to convert</param>
    /// <param name="ct">Cancellation Token to use if the user kills the task</param>
    /// <returns>List of entries</returns>
    private static async Task<List<FolderEntry>> ConvertToDiffEntriesAsync(FolderDiffResult folderDiff, CancellationToken ct)
    {
        Log.Debug("Converting Results to List");
        
        var diffEntries = new ConcurrentBag<FolderEntry>();

        var addedTask = Parallel.ForEachAsync(folderDiff.AddedFiles, ct, async (file, token) =>
        {
            await ProcessFileAsync(file, DiffType.Added, false, token);
        });

        var changedTask = Parallel.ForEachAsync(folderDiff.ChangedFiles, ct, async (file, token) =>
        {
            await ProcessFileAsync(file, DiffType.Changed, false, token);
        });

        var deletedTask = Parallel.ForEachAsync(folderDiff.DeletedFiles, ct, async (file, token) =>
        {
            await ProcessFileAsync(file, DiffType.Removed, true, token);
        });

        // Await all parallel loops to complete.
        await Task.WhenAll(addedTask, changedTask, deletedTask);

        return diffEntries.ToList();
        
        async Task ProcessFileAsync(string fileRelativePath, DiffType diffType, bool readFromOld, CancellationToken token)
        {
            var basePath = readFromOld ? OldExportPath : NewExportPath;
            var fullPath = Path.Combine(basePath, fileRelativePath);
            var fileContent = File.Exists(fullPath)
                ? await File.ReadAllLinesAsync(fullPath, token)
                : [];

            diffEntries.Add(new FolderEntry
            {
                Category = JsonDiffHelper.DetectCategory(fileRelativePath),
                DiffType = diffType,
                FilePath = fileRelativePath,
                FileContent = fileContent
            });
        }
    }
    
    /// <summary>
    /// Writes the full changelog from the given list of differences
    /// </summary>
    /// <param name="folderDiffResult">Folder Results to use and convert</param>
    /// <param name="modsDiffResult">Mods Results to use and add</param>
    /// <param name="outputPath">Path to print the changelog to</param>
    /// <param name="ct">Cancellation Token to use if the user kills the task</param>
    private static async Task WriteFullChangelog(
        FolderDiffResult folderDiffResult,
        ModsDiffResult modsDiffResult,
        string outputPath,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        Log.Information("Writing changelog");
            
        var sb = new StringBuilder();
        sb.AppendLine("# Changelog");
        sb.AppendLine();

        sb.AppendLine(await WriteModsDiffSectionAsync(modsDiffResult, ct));

        var folderEntries = await ConvertToDiffEntriesAsync(folderDiffResult, ct);


        // Define the categories in the desired order.
        var categories = new (JsonDiffHelper.DiffCategory cat, string catName, string catIcon)[]
        {
            (JsonDiffHelper.DiffCategory.Recipes, "Recipes", "🍳"),
            (JsonDiffHelper.DiffCategory.Tags, "Tags", "🏷️"),
            (JsonDiffHelper.DiffCategory.Registries, "Registries", "✍️"),
            (JsonDiffHelper.DiffCategory.LootTable, "Loot Table", "🗝️"),
            (JsonDiffHelper.DiffCategory.Unknown, "Unknown", "❓")
        };

        // Process each category concurrently.
        var categoryTasks = categories.Select(async categoryTuple =>
        {
            Log.Information($"Processing {categoryTuple.catName}");
                
            var (cat, catName, catIcon) = categoryTuple;
            var categoryEntries = folderEntries.Where(d => d.Category == cat).ToList();
            if (categoryEntries.Count == 0)
                return string.Empty; // Skip if no entries for this category.

            var categorySb = new StringBuilder();
            categorySb.AppendLine($"## {catIcon} {catName}");
            categorySb.AppendLine();

            // Group entries by diff type.
            var added   = categoryEntries.Where(d => d.DiffType == DiffType.Added).ToList();
            var changed = categoryEntries.Where(d => d.DiffType == DiffType.Changed).ToList();
            var removed = categoryEntries.Where(d => d.DiffType == DiffType.Removed).ToList();

            // Pass the flag into WriteDiffSectionAsync
            var addedTask   = WriteDiffSectionAsync("Added",   added,   '+', catName.Equals("Registries", StringComparison.OrdinalIgnoreCase), ct);
            var changedTask = WriteDiffSectionAsync("Changed", changed, null, catName.Equals("Registries", StringComparison.OrdinalIgnoreCase), ct);
            var removedTask = WriteDiffSectionAsync("Removed", removed, '-', catName.Equals("Registries", StringComparison.OrdinalIgnoreCase), ct);

            var sections = await Task.WhenAll(addedTask, changedTask, removedTask);
            foreach (var section in sections)
            {
                categorySb.Append(section);
            }
            categorySb.AppendLine();
            return categorySb.ToString();
        }).ToList();

        var categoryResults = await Task.WhenAll(categoryTasks);
        foreach (var catContent in categoryResults)
        {
            sb.Append(catContent);
        }
            
        await File.WriteAllTextAsync(outputPath, sb.ToString(), ct);
        Log.Information($"Changelog written to {outputPath}");
        
        stopwatch.Stop();
        Log.Information($"Changelog writing took {stopwatch.ElapsedMilliseconds}ms");
    }

    private enum DiffType
    {
        Added,
        Changed,
        Removed
    }
    
    private class FolderDiffResult {
        public List<string> AddedFiles { get; } = [];
        public List<string> DeletedFiles { get; } = [];
        public List<string> ChangedFiles { get; } = [];

        public static readonly FolderDiffResult Empty = new();
    }

    private class FolderEntry
    {
        public JsonDiffHelper.DiffCategory Category { get; init; }
        public DiffType DiffType { get; init; }
        public string FilePath { get; init; } = string.Empty;
        public string[] FileContent { get; init; } = [];
    }

    private class ModsDiffResult
    {
        public List<(string Name, string Version)> AddedMods { get; } = [];
        public List<(string Name, string Version)> RemovedMods { get; } = [];
        public List<(string Name, string OldVersion, string NewVersion)> ChangedMods { get; } = [];
        
        public static readonly ModsDiffResult Empty = new();
    }

    private class ModEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";
    }
}