using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PackForge.Core.Service;

public static class CurseService
{
    private const string BaseUrl = "https://api.curseforge.com";
    private static readonly HttpClient Client = new();

    private record FileDetail(int Id, string FileName);

    /// <summary>
    /// Fetches the project data from the CurseForge API
    /// </summary>
    /// <param name="url">The project url to heck</param>
    /// <param name="folders">folders to check against</param>
    /// <param name="minecraftVersion">Minecraft version to check mods for</param>
    /// <param name="loaderType">Modloader to check against</param>
    /// <param name="apiKey">CF API key used</param>
    /// <param name="ct">CancellationToken used when the process is stopped by the user</param>
    /// <exception cref="ArgumentException">If you somehow manage to input an invalid modloader</exception>
    /// <returns>Project ID : File ID</returns>
    public static async Task<(int, int)?> FetchProjectData(
        string url,
        string[] folders,
        string minecraftVersion,
        string loaderType,
        string apiKey,
        CancellationToken ct)
    {
        var loader = loaderType switch
        {
            "NeoForge" => 6,
            "Forge" => 1,
            _ => throw new ArgumentException($"Unknown loader: {loaderType}")
        };

        var project = await GetProjectIdAndFilesAsync(url, minecraftVersion, loader, apiKey, ct);
        var projectFile = await GetFileIdByFileNameAsync(project.files, folders, ct);
        return projectFile == null ? null : (project.projectId, projectFile.Value);
    }

    private static async Task<(int projectId, List<FileDetail> files)> GetProjectIdAndFilesAsync(
        string projectLink,
        string mcVersion,
        int modLoaderType,
        string apiKey,
        CancellationToken ct)
    {
        var slug = projectLink.TrimEnd('/').Split('/').Last();
        var searchUrl = $"{BaseUrl}/v1/mods/search?gameId=432&slug={slug}";
        var searchRequest = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        searchRequest.Headers.Add("x-api-key", apiKey);

        var searchResponse = await Client.SendAsync(searchRequest, ct);
        searchResponse.EnsureSuccessStatusCode();

        await using var searchStream = await searchResponse.Content.ReadAsStreamAsync(ct);
        var searchData = await JsonDocument.ParseAsync(searchStream, cancellationToken: ct);

        if (!searchData.RootElement.TryGetProperty("data", out var searchResults)
            || searchResults.GetArrayLength() == 0)
            return (0, []);

        var projectId = searchResults[0].GetProperty("id").GetInt32();

        var filesUrl = $"{BaseUrl}/v1/mods/{projectId}/files?gameVersion={mcVersion}" + $"&modLoaderType={modLoaderType}&pageSize=50";

        var filesRequest = new HttpRequestMessage(HttpMethod.Get, filesUrl);
        filesRequest.Headers.Add("x-api-key", apiKey);

        var filesResponse = await Client.SendAsync(filesRequest, ct);
        filesResponse.EnsureSuccessStatusCode();

        await using var filesStream = await filesResponse.Content.ReadAsStreamAsync(ct);
        var filesData = await JsonDocument.ParseAsync(filesStream, cancellationToken: ct);

        if (!filesData.RootElement.TryGetProperty("data", out var filesResults)
            || filesResults.GetArrayLength() == 0)
            return (projectId, []);

        var filesList = await Task.Run(() => filesResults.EnumerateArray()
            .Select(file => new FileDetail(
                file.GetProperty("id").GetInt32(),
                file.GetProperty("fileName").GetString() ?? string.Empty
            ))
            .ToList(), ct);

        return (projectId, filesList);
    }

    private static async Task<int?> GetFileIdByFileNameAsync(List<FileDetail> files, string[] folders, CancellationToken ct)
    {
        var folderFileNames = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(folders.Where(Directory.Exists), ct, (folder, token) =>
        {
            foreach (var file in Directory.GetFiles(folder)) folderFileNames.Add(Path.GetFileName(file));
            return ValueTask.CompletedTask;
        });

        var hashSet = new HashSet<string>(folderFileNames, StringComparer.OrdinalIgnoreCase);
        var match = files.FirstOrDefault(f => hashSet.Contains(Path.GetFileName(f.FileName)));
        return match?.Id;
    }
}