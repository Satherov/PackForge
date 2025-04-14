using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PackForge.Core.Util;

namespace PackForge.Core.Builders;

public static class ConfigBuilder
{
    public static async Task GenerateModListFileAsync(string filePath, List<ModInfo> modInfos, CancellationToken ct = default)
    {
        Dictionary<string, object> modDict = new();
        foreach (ModInfo mod in modInfos)
        {
            string key = !string.IsNullOrWhiteSpace(mod.Name) ? mod.Name : Path.GetFileName(mod.FilePath);
            modDict[key] = new
            {
                modId = mod.ModId,
                version = mod.Version
            };
        }

        JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        string json = JsonSerializer.Serialize(modDict, options);

        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public static async Task GenerateBccConfig(string filePath, string? curseforgeId, string modpackName, string modpackVersion, CancellationToken ct = default)
    {
        if (Validator.IsNullOrWhiteSpace(filePath) || Validator.IsNullOrWhiteSpace(modpackName) || Validator.IsNullOrWhiteSpace(modpackVersion))
            return;

        string tomlContent = $"""
                              [general]
                              	modpackProjectID = {curseforgeId ?? "000000"}
                              	modpackName = "{modpackName}"
                              	modpackVersion = "{modpackVersion}"
                              	useMetadata = false
                              """;

        await File.WriteAllTextAsync(filePath, tomlContent, ct);
    }
}