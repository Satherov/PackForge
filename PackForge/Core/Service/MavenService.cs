using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using PackForge.Core.Helpers;
using Serilog;
using static System.String;

namespace PackForge.Core.Service;

public static partial class MavenService
{
    private static readonly HttpClient HttpClient = new();
    private const string NeoForgeMavenBaseUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/";
    private const string ForgeMavenBaseUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/";

    public static async Task<List<string>> FetchAvailableVersions(string loaderType, string mcVersion)
    {
        try
        {
            var metadataUrl = loaderType switch
            {
                "NeoForge" => NeoForgeMavenBaseUrl,
                "Forge" => ForgeMavenBaseUrl,
                _ => throw new ArgumentException($"Unknown loader: {loaderType}")
            };

            var xmlContent = await HttpClient.GetStringAsync(metadataUrl + "maven-metadata.xml");
            var versionMatches = XDocument.Parse(xmlContent)
                .Descendants("version")
                .Select(e => e.Value)
                .ToList();

            if (loaderType.Equals("NeoForge"))
            {
                Log.Debug($"mcVersion: {mcVersion}");
                
                var versionParts = mcVersion.Split('.');
                mcVersion = versionParts.Length switch
                {
                    // versionParts[0] = 1
                    // versionParts[1] = Major
                    // versionParts[2] = Minor
                    2 => $"{versionParts[1]}.0", // 1.20 -> 20.0
                    3 => $"{versionParts[1]}.{versionParts[2]}", // 1.20.2 -> 20.2
                    _ => $"{versionParts[1]}.0"
                };
                
                Log.Debug($"mcVersion: {mcVersion}");
            }

            var expectedPrefix = loaderType switch
            {
                "NeoForge" => $"{mcVersion}.",
                "Forge" => $"{mcVersion}-",
                _ => ""
            };
            
            Log.Debug($"Expected prefix: {expectedPrefix}");
            
            var filteredVersions = versionMatches.Where(v => v.StartsWith(expectedPrefix)).ToList();

            if (filteredVersions.Count == 0) throw new InvalidOperationException($"No versions found matching Minecraft version {mcVersion} in Maven metadata XML");
            
            Log.Information($"Successfully returned available versions from maven for {loaderType} {mcVersion}");
            return filteredVersions;

        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch available Loader versions from Maven: {ex.Message}");
            return [];
        }
    }


    public static async Task DownloadLoader(string? loaderType, string? loaderVersion, string? savePath)
    {
        if(Validator.IsNullOrWhiteSpace(loaderType) ||
           Validator.IsNullOrWhiteSpace(loaderVersion) ||
           !Validator.DirectoryExists(savePath)) return;
        
        var loader = Join("-", loaderType!, loaderVersion);
        
        Log.Information($"Downloading {loader} to {savePath}");

        var installerUrl = loaderType switch
        {
            "NeoForge" => $"{NeoForgeMavenBaseUrl}{loaderVersion}/{loader.ToLowerInvariant()}-installer.jar",
            "Forge" => $"{ForgeMavenBaseUrl}{loaderVersion}/{loader.ToLowerInvariant()}-installer.jar",
            _ => throw new ArgumentException($"Unknown loader: {loaderType}")
        };

        var destinationFile = loaderType switch
        {
            "NeoForge" => Path.Join(savePath!, $"{loader}-installer.jar"),
            "Forge" => Path.Join(savePath!, $"{loader}-installer.jar"),
            _ => throw new ArgumentException($"Unknown loader: {loaderType}")
        };

        try
        {
            var fileBytes = await HttpClient.GetByteArrayAsync(installerUrl);
            await File.WriteAllBytesAsync(destinationFile, fileBytes);

            Log.Information($"{loader} downloaded to: {destinationFile}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to download {loader}: {ex.Message}");
        }
    }


    [GeneratedRegex(@"<version>(?<version>\d+\.\d+\.\d+(?:\.\d+)?(?:-[a-zA-Z0-9.\-]+)?)</version>")]
    private static partial Regex Version();
}