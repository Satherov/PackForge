using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Tomlyn;
using Tomlyn.Model;

namespace PackForge.Core.Util;

public record ModMetadata(string ModId, string DisplayName, string Version, List<string> Authors, List<string> Dependencies)
{
    public string ModId { get; set; } = ModId;
    public string DisplayName { get; set; } = DisplayName;
    public string Version { get; set; } = Version;
    public List<string> Authors { get; set; } = Authors;
    public List<string> Dependencies { get; set; } = Dependencies;
    public static ModMetadata Empty => new(string.Empty, string.Empty, string.Empty, [], []);
}

public record ModInfo(string FilePath, ModMetadata Metadata, int ClassFileCount, bool IsMcreator, bool OnlyJarInJars, List<ModInfo> NestedJars)
{
    public string FilePath { get; set; } = FilePath;
    public ModMetadata Metadata { get; set; } = Metadata;
    public int ClassFileCount { get; set; } = ClassFileCount;
    public bool IsMcreator { get; set; } = IsMcreator;
    public bool OnlyJarInJars { get; set; } = OnlyJarInJars;
    public List<ModInfo> NestedJars { get; set; } = NestedJars;

    public static ModInfo NoData(string path)
    {
        return new ModInfo(path, ModMetadata.Empty, 0, false, false, []);
    }

    public override string ToString()
    {
        return
            $"{Path.GetFileName(FilePath)} '{Metadata.DisplayName} - {Metadata.Version}' ({Metadata.ModId}) by {string.Join(", ", Metadata.Authors)} [{ClassFileCount} classes] [Mcreator: {IsMcreator}] [JarInJars: {NestedJars.Count}]";
    }
}

public static class JarUtil
{
    public static async Task<List<ModInfo>> GetJarInfoInDirectoryAsync(string directory, CancellationToken ct = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        IEnumerable<string> jarFiles = Directory.EnumerateFiles(directory, "*.jar", SearchOption.AllDirectories);

        List<Task<ModInfo>> tasks = jarFiles.Select(jarFilePath => Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            ModInfo modInfo = await ProcessJarFileAsync(jarFilePath, ct);
            return modInfo;
        }, ct)).ToList();

        ModInfo[] results = await Task.WhenAll(tasks);
        stopwatch.Stop();
        Log.Debug("Collected mod data from {Directory} in {StopwatchElapsedMilliseconds} ms", directory, stopwatch.ElapsedMilliseconds);
        return results.ToList();
    }

    private static async Task<ModInfo> ProcessJarFileAsync(string jarFilePath, CancellationToken ct)
    {
        using ZipArchive archive = ZipFile.OpenRead(jarFilePath);
        ModInfo modInfo = await ReadJarInfo(archive, jarFilePath, ct);
        await ProcessNestedJarsAsync(archive, jarFilePath, modInfo, ct);
        return modInfo;
    }

    private static async Task ProcessNestedJarsAsync(ZipArchive archive, string jarFilePath, ModInfo modInfo, CancellationToken ct)
    {
        List<ZipArchiveEntry> nestedJarEntries = archive.Entries.Where(entry =>
            entry.FullName.StartsWith("META-INF/jarjar", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).ToList();

        ConcurrentBag<ModInfo> nestedJars = [];

        IEnumerable<Task> tasks = nestedJarEntries.Select(async jarEntry =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using Stream stream = jarEntry.Open();
                using ZipArchive nestedArchive = new(stream, ZipArchiveMode.Read);
                ModInfo nestedModInfo = await ReadJarInfo(nestedArchive, jarFilePath, ct);
                nestedJars.Add(nestedModInfo);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to process nested jar {JarEntryFullName} in {JarFilePath}: {ExMessage}", jarEntry.FullName, jarFilePath, ex.Message);
            }
        });

        await Task.WhenAll(tasks);
        modInfo.NestedJars = nestedJars.ToList();
    }


    private static async Task<ModInfo> ReadJarInfo(ZipArchive archive, string jarFilePath, CancellationToken ct)
    {
        Log.Debug("Reading jar file {GetFileName}", Path.GetFileName(jarFilePath));

        ModInfo modInfo = ModInfo.NoData(jarFilePath);
        ZipArchiveEntry? tomlEntry = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith("mods.toml", StringComparison.OrdinalIgnoreCase));

        if (tomlEntry != null)
        {
            ModMetadata tomlData = await ReadTomlDataAsync(tomlEntry, ct);
            modInfo.Metadata = tomlData;
        }
        else
        {
            modInfo.Metadata = ModMetadata.Empty;
        }

        await CountClassFilesAsync(archive, modInfo, ct);
        return modInfo;
    }

    private static async Task<ModMetadata> ReadTomlDataAsync(ZipArchiveEntry tomlEntry, CancellationToken ct)
    {
        ModMetadata tomlData = ModMetadata.Empty;
        await using Stream stream = tomlEntry.Open();
        using StreamReader reader = new(stream);
        string tomlText = await reader.ReadToEndAsync(ct);
        TomlTable model = Toml.Parse(tomlText).ToModel();

        if (!model.TryGetValue("mods", out object? modsSection) || modsSection is not TomlTableArray { Count: > 0 } modTable) return tomlData;

        TomlTable modData = modTable[0];
        tomlData.ModId = modData["modId"]?.ToString() ?? string.Empty;
        tomlData.Version = modData["version"]?.ToString() ?? string.Empty;
        tomlData.DisplayName = modData["displayName"]?.ToString() ?? string.Empty;
        tomlData.Authors = (modData.TryGetValue("authors", out object value) ? value : null) switch
        {
            string authorString => authorString.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).ToList(),
            TomlTableArray authorArray => authorArray.OfType<object?>().Select(a => a?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()!,
            _ => []
        };

        return tomlData;
    }

    private static async Task CountClassFilesAsync(ZipArchive archive, ModInfo modInfo, CancellationToken ct)
    {
        IEnumerable<ZipArchiveEntry> validEntries = archive.Entries.Where(entry =>
            !entry.FullName.StartsWith("assets", StringComparison.OrdinalIgnoreCase) && !entry.FullName.StartsWith("data", StringComparison.OrdinalIgnoreCase));

        int classCount = 0;
        await Parallel.ForEachAsync(validEntries, ct, (file, token) =>
        {
            token.ThrowIfCancellationRequested();

            if (file.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref classCount);
            else if (file.FullName.Contains("mcreator", StringComparison.OrdinalIgnoreCase) || file.FullName.Contains("procedures", StringComparison.OrdinalIgnoreCase))
                modInfo.IsMcreator = true;

            return ValueTask.CompletedTask;
        });

        modInfo.ClassFileCount = classCount;
    }
}