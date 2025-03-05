using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;

namespace PackForge.Core.Service;

public static partial class MavenService
{
    private static readonly HttpClient HttpClient = new();
    private const string NeoForgeMavenBaseUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/";
    private const string ForgeMavenBaseUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/";

    public static async Task<List<string>> FetchAvailableVersions(string loader, string version)
    {
        try
        {
            var metadataUrl = loader switch
            {
                "NeoForge" => NeoForgeMavenBaseUrl,
                "Forge" => ForgeMavenBaseUrl,
                _ => throw new ArgumentException($"Unknown loader: {loader}")
            };

            var xmlContent = await HttpClient.GetStringAsync(metadataUrl + "maven-metadata.xml");

            var versionMatches = Version().Matches(xmlContent)
                .Select(m => m.Groups["version"].Value)
                .ToList();

            if (loader.Equals("NeoForge"))
            {
                var versionParts = version.Split('.');
                version = versionParts.Length switch
                {
                    1 => $"{version}.0.0",
                    2 => $"{version}.0",
                    _ => version
                };
            }

            var expectedPrefix = loader switch
            {
                "NeoForge" => $"{version.Split('.')[1]}.{version.Split('.')[2]}",
                "Forge" => $"{version}-",
                _ => ""
            };

            var filteredVersions = versionMatches
                .Where(v => v.StartsWith(expectedPrefix))
                .ToList();

            if (filteredVersions.Count != 0) return filteredVersions;

            throw new InvalidOperationException(
                $"No versions found matching Minecraft version {version} in Maven metadata XML");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch available Loader versions from Maven: {ex.Message}");
            return [];
        }
    }


    public static async Task DownloadLoader(string loader, string version, string? savePath)
    {
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(savePath))
        {
            Log.Warning("Version or save path is invalid");
            return;
        }

        var installerUrl = loader switch
        {
            "NeoForge" => $"{NeoForgeMavenBaseUrl}{version}/neoforge-{version}-installer.jar",
            "Forge" => $"{ForgeMavenBaseUrl}{version}/forge-{version}-installer.jar",
            _ => throw new ArgumentException($"Unknown loader: {loader}")
        };

        var destinationFile = loader switch
        {
            "NeoForge" => Path.Combine(savePath, $"neoforge-{version}-installer.jar"),
            "Forge" => Path.Combine(savePath, $"forge-{version}-installer.jar"),
            _ => throw new ArgumentException($"Unknown loader: {loader}")
        };

        try
        {
            Log.Information($"Downloading Modloader {version}");
            var fileBytes = await HttpClient.GetByteArrayAsync(installerUrl);
            await File.WriteAllBytesAsync(destinationFile, fileBytes);

            Log.Information($"Modlaoder {version} downloaded to: {destinationFile}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to download Modloader {version}: {ex.Message}");
        }
    }


    [GeneratedRegex(@"<version>(?<version>\d+\.\d+\.\d+(?:\.\d+)?(?:-[a-zA-Z0-9.\-]+)?)</version>")]
    private static partial Regex Version();
}