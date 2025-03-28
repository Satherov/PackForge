﻿using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PackForge.Core.Helpers;
using Serilog;

namespace PackForge.Core.Builders;

public abstract class JsonBuilder
{
    public static async Task GenerateManifest(
        string path,
        string mcVersion = "1.0.0",
        string packVersion = "0.0.0",
        string packAuthor = "Unknown",
        string packName = "Unknown Modpack",
        string loaderType = "unknown loader",
        string loaderVersion = "0.0.0",
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Log.Information($"Generating manifest");
        
        if(!Validator.DirectoryExists(path)) return;

        var mcInstance = Path.Combine(path, "minecraftinstance.json");
        if(!Validator.FileExists(mcInstance, path)) return;
        
        var files = await ProcessMinecraftInstanceAsync(mcInstance, path, ct);
        if (files.Count == 0)
        {
            Log.Warning("Could not read from minecraftinstance.json");
            return;
        }
        
        var manifest = new JObject
        {
            ["minecraft"] = new JObject
            {
                ["version"] = mcVersion,
                ["modLoaders"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = string.Join("-", loaderType.ToLowerInvariant(), loaderVersion),
                        ["primary"] = true,
                    }
                }
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
        Log.Information($"Manifest generation took {stopwatch.ElapsedMilliseconds}ms");
    }

    private static async Task<JArray> ProcessMinecraftInstanceAsync(string pathToJson, string path, CancellationToken ct)
    {
        Log.Information($"Processing minecraft instance file {pathToJson}");
        
        var modsFolder = Path.Combine(path, "mods");
        if (!Validator.DirectoryExists(modsFolder)) return [];
        
        var shaderpacksFolder = Path.Combine(path, "shaderpacks");
        Validator.DirectoryExists(shaderpacksFolder, logLevel: "debug");
        
        var resourcepacksFolder = Path.Combine(path, "resourcepacks");
        Validator.DirectoryExists(resourcepacksFolder, logLevel: "debug");
        
        var jsonContent = await File.ReadAllTextAsync(pathToJson, ct);
        var root = JObject.Parse(jsonContent);
        
        var installedAddons = (JArray)root["installedAddons"]!.DeepClone();
        var matchedEntries = new JArray();
        
        foreach (var folder in new[] { modsFolder, shaderpacksFolder, resourcepacksFolder }.Where(value => !string.IsNullOrEmpty(value)))
        {
            if (Validator.DirectoryExists(folder, logLevel: "debug"))
            {
                foreach (var addon in ProcessFolderAsync(folder, installedAddons, ct))
                {
                    matchedEntries.Add(addon);
                }
            }
            else
            {
                Log.Debug($"{Path.GetFileName(folder)} folder doesnt exist");
            }

            if (installedAddons.Count <= 0)
            {
                Log.Debug("All entries matched");
                break;
            }
            
            Log.Debug("Unmatched entries:");
            foreach (var leftover in installedAddons)
            {
                Log.Debug($"fileName: {leftover["installedFile"]?["fileName"]}, addonID: {leftover["addonID"]}, fileID: {leftover["installedFile"]?["id"]}");
            }
        }
            
        File.Delete(pathToJson);
        return matchedEntries;
    }
    
    private static  JArray ProcessFolderAsync(string folderPath, JArray installedAddons, CancellationToken ct)
    {
        Log.Debug($"Checking {folderPath}");
        
        var matchedEntries = new JArray();
        var enumerateFiles = Directory.EnumerateFiles(folderPath).ToList();
        
        if (enumerateFiles.Count == 0)
        {
            Log.Debug($"No files found in {Path.GetFileName(folderPath)}");
            return matchedEntries;
        }
        
        foreach (var filePath in enumerateFiles)
        {
            if (ct.IsCancellationRequested) break;
            
            var fileName = Path.GetFileName(filePath);

            var match = installedAddons.FirstOrDefault(x => (string)x["installedFile"]?["fileName"]! == fileName);
            if (match == null) continue;
            matchedEntries.Add(new JObject
            {
                ["projectID"] = match["addonID"],
                ["fileID"] = match["installedFile"]?["id"],
                ["required"] = true
            });

            Log.Debug($"Match found for {fileName}: addonID: {match["addonID"]}, fileID: {match["installedFile"]?["id"]}");
            installedAddons.Remove(match);
            File.Delete(filePath);

        }

        return matchedEntries;
    }
}