using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PackForge.Core.Util;
using Serilog;
using Serilog.Events;

namespace PackForge.Core.Builders;

public abstract class JsonBuilder
{
    public static async Task GenerateManifest(string path, string mcVersion = "1.0.0", string packVersion = "0.0.0", string packAuthor = "Unknown",
        string packName = "Unknown Modpack", string loaderType = "Unknown Loader", string loaderVersion = "0.0.0", int recommendedRam = 0, CancellationToken ct = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Log.Information($"Generating manifest");

        if (!Validator.DirectoryExists(path)) return;

        string mcInstance = Path.Combine(path, "minecraftinstance.json");
        if (!Validator.FileExists(mcInstance)) return;

        JArray files = await ProcessMinecraftInstanceAsync(mcInstance, path, ct);
        if (files.Count == 0)
        {
            Log.Warning("Could not read from minecraftinstance.json");
            return;
        }

        JObject manifest = new()
        {
            ["minecraft"] = new JObject
            {
                ["version"] = mcVersion,
                ["modLoaders"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = SimplifyVersionString(string.Join("-", loaderType.ToLowerInvariant(), loaderVersion)),
                        ["primary"] = true
                    }
                },
                ["recommendedRam"] = recommendedRam
            },
            ["manifestType"] = "minecraftModpack",
            ["manifestVersion"] = 1,
            ["name"] = packName,
            ["version"] = packVersion,
            ["author"] = packAuthor,
            ["files"] = files,
            ["overrides"] = "overrides"
        };

        await File.WriteAllTextAsync(Path.Combine(path, "manifest.json"), manifest.ToString(), ct);

        stopwatch.Stop();
        Log.Information("Manifest generation took {StopwatchElapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);
    }

    private static async Task<JArray> ProcessMinecraftInstanceAsync(string pathToJson, string path, CancellationToken ct)
    {
        Log.Information("Processing {GetFileName}", Path.GetFileName(pathToJson));

        string modsFolder = Path.Combine(path, "mods");
        if (!Validator.DirectoryExists(modsFolder)) return [];

        string shaderpacksFolder = Path.Combine(path, "shaderpacks");
        Validator.DirectoryExists(shaderpacksFolder, LogEventLevel.Debug);

        string resourcepacksFolder = Path.Combine(path, "resourcepacks");
        Validator.DirectoryExists(resourcepacksFolder, LogEventLevel.Debug);

        string jsonContent = await File.ReadAllTextAsync(pathToJson, ct);
        JObject root = JObject.Parse(jsonContent);

        JArray installedAddons = (JArray)root["installedAddons"]!.DeepClone();
        JArray matchedEntries = [];

        foreach (string folder in new[] { modsFolder, shaderpacksFolder, resourcepacksFolder }.Where(value => !string.IsNullOrEmpty(value)))
        {
            if (Validator.DirectoryExists(folder, LogEventLevel.Debug))
                foreach (JToken addon in ProcessFolderAsync(folder, installedAddons, ct))
                    matchedEntries.Add(addon);
            else
                Log.Debug("{GetFileName} folder doesnt exist", Path.GetFileName(folder));

            if (installedAddons.Count <= 0)
            {
                Log.Debug("All entries matched");
                break;
            }

            Log.Debug("Unmatched entries:");
            foreach (JToken leftover in installedAddons)
                Log.Debug("fileName: {JToken}, addonID: {JToken1}, fileID: {JToken2}", leftover["installedFile"]?["fileName"], leftover["addonID"], leftover["installedFile"]?["id"]);
        }

        File.Delete(pathToJson);
        return matchedEntries;
    }

    private static JArray ProcessFolderAsync(string folderPath, JArray installedAddons, CancellationToken ct)
    {
        Log.Debug("Checking {FolderPath}", folderPath);

        JArray matchedEntries = [];
        List<string> enumerateFiles = Directory.EnumerateFiles(folderPath).ToList();

        if (enumerateFiles.Count == 0)
        {
            Log.Debug("No files found in {GetFileName}", Path.GetFileName(folderPath));
            return matchedEntries;
        }

        foreach (string filePath in enumerateFiles)
        {
            if (ct.IsCancellationRequested) break;

            string fileName = Path.GetFileName(filePath);

            JToken? match = installedAddons.FirstOrDefault(x => (string)x["installedFile"]?["fileName"]! == fileName);
            if (match == null) continue;
            matchedEntries.Add(new JObject
            {
                ["projectID"] = match["addonID"],
                ["fileID"] = match["installedFile"]?["id"],
                ["required"] = true
            });

            Log.Debug("Match found for {FileName}: addonID: {JToken}, fileID: {JToken1}", fileName, match["addonID"], match["installedFile"]?["id"]);
            installedAddons.Remove(match);
            File.Delete(filePath);
        }

        return matchedEntries;
    }
    
    private static string SimplifyVersionString(string input)
    {
        string[] parts = input.Split('-');
        return parts.Length > 2 ? $"{parts[0]}-{parts[^1]}" : input;
    }
}