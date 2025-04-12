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
    public static async Task<(int, int)?> FetchProjectData(string url, string[] folders, string minecraftVersion, string loaderType, string apiKey, CancellationToken ct)
    {
        int loader = loaderType switch
        {
            "NeoForge" => 6,
            "Forge" => 1,
            _ => throw new ArgumentException($"Unknown loader: {loaderType}")
        };

        (int projectId, List<FileDetail> files) project = await GetProjectIdAndFilesAsync(url, minecraftVersion, loader, apiKey, ct);
        int? projectFile = await GetFileIdByFileNameAsync(project.files, folders, ct);
        return projectFile == null ? null : (project.projectId, projectFile.Value);
    }

    private static async Task<(int projectId, List<FileDetail> files)> GetProjectIdAndFilesAsync(string projectLink, string mcVersion, int modLoaderType, string apiKey,
        CancellationToken ct)
    {
        string slug = projectLink.TrimEnd('/').Split('/').Last();
        string searchUrl = $"{BaseUrl}/v1/mods/search?gameId=432&slug={slug}";
        HttpRequestMessage searchRequest = new(HttpMethod.Get, searchUrl);
        searchRequest.Headers.Add("x-api-key", apiKey);

        HttpResponseMessage searchResponse = await Client.SendAsync(searchRequest, ct);
        searchResponse.EnsureSuccessStatusCode();

        await using Stream searchStream = await searchResponse.Content.ReadAsStreamAsync(ct);
        JsonDocument searchData = await JsonDocument.ParseAsync(searchStream, cancellationToken: ct);

        if (!searchData.RootElement.TryGetProperty("data", out JsonElement searchResults) || searchResults.GetArrayLength() == 0)
            return (0, []);

        int projectId = searchResults[0].GetProperty("id").GetInt32();

        string filesUrl = $"{BaseUrl}/v1/mods/{projectId}/files?gameVersion={mcVersion}" + $"&modLoaderType={modLoaderType}&pageSize=50";

        HttpRequestMessage filesRequest = new(HttpMethod.Get, filesUrl);
        filesRequest.Headers.Add("x-api-key", apiKey);

        HttpResponseMessage filesResponse = await Client.SendAsync(filesRequest, ct);
        filesResponse.EnsureSuccessStatusCode();

        await using Stream filesStream = await filesResponse.Content.ReadAsStreamAsync(ct);
        JsonDocument filesData = await JsonDocument.ParseAsync(filesStream, cancellationToken: ct);

        if (!filesData.RootElement.TryGetProperty("data", out JsonElement filesResults) || filesResults.GetArrayLength() == 0)
            return (projectId, []);

        List<FileDetail> filesList =
            await Task.Run(
                () => filesResults.EnumerateArray().Select(file => new FileDetail(file.GetProperty("id").GetInt32(), file.GetProperty("fileName").GetString() ?? string.Empty))
                    .ToList(), ct);

        return (projectId, filesList);
    }

    private static async Task<int?> GetFileIdByFileNameAsync(List<FileDetail> files, string[] folders, CancellationToken ct)
    {
        ConcurrentBag<string> folderFileNames = [];

        await Parallel.ForEachAsync(folders.Where(Directory.Exists), ct, (folder, token) =>
        {
            foreach (string file in Directory.GetFiles(folder)) folderFileNames.Add(Path.GetFileName(file));
            return ValueTask.CompletedTask;
        });

        HashSet<string> hashSet = new(folderFileNames, StringComparer.OrdinalIgnoreCase);
        FileDetail? match = files.FirstOrDefault(f => hashSet.Contains(Path.GetFileName(f.FileName)));
        return match?.Id;
    }
}