using System;
using System.Collections.Generic;
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

public record ModInfo(
    string FilePath,
    string Name,
    string ModId,
    string Version,
    List<string> Authors,
    int Classes,
    bool McreatorFragments,
    bool OnlyJarInJars,
    List<ModInfo> JarInJars)
{
    public string FilePath { get; set; } = FilePath;
    public string Name { get; set; } = Name;
    public string ModId { get; set; } = ModId;
    public string Version { get; set; } = Version;
    public List<string> Authors { get; set; } = Authors;
    public int Classes { get; set; } = Classes;
    public bool McreatorFragments { get; set; } = McreatorFragments;
    public bool OnlyJarInJars { get; set; }
    public List<ModInfo> JarInJars { get; set; } = JarInJars;

    public static ModInfo Empty => new(string.Empty, string.Empty, string.Empty, string.Empty, [], -1, false, false, []);

    public static ModInfo NoData(string path)
    {
        return new ModInfo(path, string.Empty, string.Empty, string.Empty, [], -1, false, false, []);
    }

    public static ModInfo JarInJarOnly(string path, List<ModInfo> jarInJars)
    {
        return new ModInfo(path, string.Empty, string.Empty, string.Empty, [], -1, false, false, jarInJars);
    }

    public override string ToString()
    {
        return $"{Path.GetFileName(FilePath)}: ({ModId}) {Name}-{Version} by '{string.Join(", ", Authors)}' [Classes: {Classes}] [MCreator: {McreatorFragments}]";
    }
}

public static class JarHelper
{
    private static List<ModInfo> _cachedModData = [];
    private static string _cachedModFolderHash = string.Empty;

    public static async Task<List<ModInfo>> GetAllModData(string sourceDir, CancellationToken ct = default)
    {
        if (!Validator.DirectoryExists(sourceDir))
            return [];

        string currentHash = ComputeFolderHash(sourceDir);

        if (_cachedModData.Count != 0 && _cachedModFolderHash == currentHash)
            return _cachedModData;

        await GatherAllModData(sourceDir, ct);
        _cachedModFolderHash = currentHash;
        return _cachedModData;
    }

    private static async Task GatherAllModData(string sourceDir, CancellationToken ct = default)
    {
        if (!Validator.DirectoryExists(sourceDir))
            return;

        IEnumerable<string> entries = Directory.EnumerateFiles(sourceDir);
        List<ModInfo> data = [];

        await Parallel.ForEachAsync(entries, ct, async (entry, token) =>
        {
            ModInfo modData = await GatherModData(entry, token);
            lock (data)
            {
                data.Add(modData);
            }
        });

        _cachedModData = data;
    }

    private static async Task<ModInfo> GatherModData(string jarFilePath, CancellationToken ct = default)
    {
        if (!Validator.FileExists(jarFilePath))
            return ModInfo.Empty;

        using ZipArchive archive = ZipFile.OpenRead(jarFilePath);
        return await ProcessArchive(archive, jarFilePath, ct);
    }

    private static async Task<ModInfo> ProcessArchive(ZipArchive archive, string jarFilePath, CancellationToken ct)
    {
        ModInfo info = ModInfo.NoData(jarFilePath);

        ZipArchiveEntry? tomlEntry = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.Contains("META-INF", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith("mods.toml", StringComparison.OrdinalIgnoreCase));

        if (tomlEntry != null)
        {
            await using Stream stream = tomlEntry.Open();
            using StreamReader reader = new(stream);
            StringBuilder contentBuilder = new();
            while (await reader.ReadLineAsync(ct) is { } line) contentBuilder.AppendLine(line);
            string content = contentBuilder.ToString();

            if (!string.IsNullOrWhiteSpace(content))
            {
                TomlTable tomlModel = Toml.Parse(content).ToModel();
                if (tomlModel.TryGetValue("mods", out object? modsObj) && modsObj is TomlTableArray { Count: > 0 } modsArray)
                {
                    TomlTable modEntry = modsArray[0];
                    modEntry.TryGetValue("displayName", out object? displayName);
                    info.Name = displayName?.ToString() ?? "empty / parse failed";

                    modEntry.TryGetValue("modId", out object? modId);
                    info.ModId = modId?.ToString() ?? "empty / parse failed";

                    modEntry.TryGetValue("version", out object? version);
                    info.Version = version?.ToString() ?? "empty / parse failed";

                    modEntry.TryGetValue("authors", out object? authors);
                    info.Authors = authors switch
                    {
                        string authorString => authorString.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).ToList(),
                        TomlTableArray authorArray => authorArray.OfType<object?>().Select(a => a?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()!,
                        _ => ["empty / parse failed"]
                    };
                }
            }
        }
        else
        {
            info.OnlyJarInJars = true;
        }


        List<ZipArchiveEntry> jarInJarEntries = archive.Entries.Where(entry =>
            entry.FullName.Contains("META-INF/jarjar", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (ZipArchiveEntry jarEntry in jarInJarEntries)
        {
            await using Stream jarStream = jarEntry.Open();
            using ZipArchive nestedArchive = new(jarStream);
            ModInfo nestedInfo = await ProcessArchive(nestedArchive, jarEntry.FullName, ct);
            info.JarInJars.Add(nestedInfo);
        }

        bool mcreatorFound = false;

        foreach (string file in archive.Entries.ToList().Select(entry => entry.FullName))
        {
            Log.Debug(file);

            if (file.EndsWith(".class", StringComparison.OrdinalIgnoreCase)) info.Classes++;

            string[] segments = file.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

            if (!mcreatorFound && (segments.Any(s => s.Equals("mcreator", StringComparison.OrdinalIgnoreCase)) ||
                                   segments.Any(s => s.Equals("procedures", StringComparison.OrdinalIgnoreCase))))
            {
                mcreatorFound = true;
                info.McreatorFragments = true;
            }
        }

        return info;
    }

    private static string ComputeFolderHash(string folderPath)
    {
        string[] files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
        Array.Sort(files);

        StringBuilder sb = new();
        foreach (string file in files)
        {
            FileInfo info = new(file);
            sb.Append($"{file}:{info.LastWriteTimeUtc.Ticks}:{info.Length};");
        }

        byte[] inputBytes = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] hashBytes = System.Security.Cryptography.MD5.HashData(inputBytes);
        return Convert.ToHexStringLower(hashBytes);
    }
}