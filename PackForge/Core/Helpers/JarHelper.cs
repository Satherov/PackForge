using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Serilog;
using Tomlyn;
using Tomlyn.Model;

namespace PackForge.Core.Helpers;

public class JarHelper
{
    public static async Task<Dictionary<string, string>?> ExtractModMetadata(string jarFilePath, string loaderType)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarFilePath);
            ZipArchiveEntry? entry;
            switch (loaderType)
            {
                case "neoforge":
                    entry = archive.GetEntry("META-INF/neoforge.mods.toml");
                    break;

                case "forge":
                    entry = archive.GetEntry("META-INF/mods.toml");
                    break;

                default:
                    Log.Warning($"Unknown loader type: {loaderType}");
                    entry = null;
                    break;
            }

            if(Validator.CheckNullOrWhiteSpace(entry, message: $"TOML file not found in {jarFilePath}", logLevel: "error")) return null;

            using var reader = new StreamReader(entry!.Open());
            var content = await reader.ReadToEndAsync();

            if (!Toml.ToModel(content).TryGetValue("mods", out var value1) || value1 is not TomlArray modsSection ||
                modsSection.Count == 0)
                return null;

            if (modsSection[0] is not TomlTable firstMod) return null;
            var metadata = new Dictionary<string, string>();

            foreach (var key in firstMod.Keys)
                if (firstMod.TryGetValue(key, out var value))
                    metadata[key] = value.ToString() ?? "null";

            return metadata;
        }
        catch (Exception ex)
        {
            Log.Error($"Error reading JAR metadata: {ex.Message}");
            return null;
        }
    }
}