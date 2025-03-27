using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using PackForge.Core.Data;
using PackForge.Core.Helpers;
using Serilog;

namespace PackForge.Core.Service;

public static partial class GitService
{
    private static readonly string TempRepoPath = Path.Combine(
        Path.GetTempPath(), "PackForge", "TempRepo");
    
    public static async Task<string> SilentCloneGitHubRepo(string? gitHubUrl, CancellationToken cts)
    {
        Log.Debug("Starting silent clone task...");
        if (!Validator.IsNullOrWhiteSpace(gitHubUrl) && await IsValidGitHubRepoAsync(gitHubUrl!, true)) 
            return await CloneGitHubRepo(gitHubUrl!, cts, true);
        Log.Debug("Invalid GitHub repository URL provided to silent task");
        return string.Empty;
    }
    
    public static async Task<string> CloneGitHubRepo(string gitHubUrl, CancellationToken userCts, bool silent = false, int timeoutMilliseconds = 60000)
    {
        var stopwatch = Stopwatch.StartNew();
        
        if(silent) Log.Debug($"Processing GitHub repository: {gitHubUrl}");
        else Log.Information($"Processing GitHub repository: {gitHubUrl}");
        
        using var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, userCts);

        var match = GitHubRepo().Match(gitHubUrl);
        if (!match.Success)
        {
            Log.Error($"GitHub url '{gitHubUrl}' did not match pattern");
            return string.Empty;
        }
        var owner = match.Groups[1].Value;
        var repo = match.Groups[2].Value;

        var defaultBranch = "main";
        var apiUrl = $"https://api.github.com/repos/{owner}/{repo}";
        using (var httpClient = new HttpClient())
        {
            Log.Debug("Requesting default branch from GitHub API");
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            // Attempt to retrieve token
            var token = await TokenManager.RetrieveTokenAsync("GitHub");
            if (!Validator.IsNullOrWhiteSpace(token, logLevel:"debug"))
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);

            try
            {
                var response = await httpClient.GetAsync(apiUrl, linkedCts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(linkedCts.Token);
                    using var jsonDoc = JsonDocument.Parse(json);
                    if (jsonDoc.RootElement.TryGetProperty("default_branch", out var branchElement))
                        defaultBranch = branchElement.GetString() ?? defaultBranch;
                    Log.Debug($"Returned default branch: {defaultBranch}");
                }
                else
                {
                    Log.Warning($"GitHub API returned {response.StatusCode}. Using '{defaultBranch}'");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"API error: {ex.Message}. Using '{defaultBranch}'");
            }
        }

        if (Validator.DirectoryExists(GetTempPath()))
        {
            if (Repository.IsValid(GetTempRepoPath()))
            {
                try
                {
                    using var repoInstance = new Repository(GetTempRepoPath());
                    var remote = repoInstance.Network.Remotes["origin"];
                    Commands.Fetch(repoInstance, remote.Name, remote.FetchRefSpecs.Select(x => x.Specification), null, null);
                    var remoteBranch = repoInstance.Branches[$"origin/{defaultBranch}"];
                    if (remoteBranch != null && repoInstance.Head.Tip.Sha == remoteBranch.Tip.Sha)
                    {
                        Log.Debug("Repository is already up-to-date. Skipping clone.");
                        return GetTempRepoPath();
                    }
                    else
                    {
                        Log.Debug("Repository is outdated. Deleting old repository.");
                        await DeleteTempRepo(silent);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error checking repository status: {ex.Message}");
                    await DeleteTempRepo(silent);
                }
            }
            else
            {
                await DeleteTempRepo(silent);
            }
        }

        Log.Debug("Creating new temp repo");
        Directory.CreateDirectory(GetTempRepoPath());

        var cloneOptions = new CloneOptions
        {
            FetchOptions = { Depth = 1 },
            BranchName = defaultBranch
        };

        try
        {
            await Task.Run(() =>
            {
                Repository.Clone(gitHubUrl, GetTempRepoPath(), cloneOptions);
            }, linkedCts.Token);

            Log.Information($"Successfully cloned GitHub repository: {gitHubUrl}");
            return GetTempRepoPath();
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Operation canceled");
        }
        catch (Exception ex)
        {
            Log.Error($"Clone failed: {ex.Message}");
        }
        
        stopwatch.Stop();
        if(silent) Log.Debug($"GitHub repository cloned in {stopwatch.ElapsedMilliseconds}ms");
        else Log.Information($"GitHub repository cloned in {stopwatch.ElapsedMilliseconds}ms");

        return string.Empty;
    }

    private static async Task DownloadDirectoryRecursive(HttpClient client, string apiUrl, string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);
        var json = await client.GetStringAsync(apiUrl);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
            throw new Exception("Expected directory contents as a JSON array.");

        foreach (var item in root.EnumerateArray())
        {
            var type = item.GetProperty("type").GetString();
            var name = item.GetProperty("name").GetString();
            var localPath = Path.Combine(destinationFolder, name!);

            Log.Debug($"Processing: {name}");

            switch (type)
            {
                case "file":
                {
                    var downloadUrl = item.GetProperty("download_url").GetString();
                    if (string.IsNullOrEmpty(downloadUrl))
                        continue;

                    var remoteSha = item.GetProperty("sha").GetString();

                    if (File.Exists(localPath))
                    {
                        try
                        {
                            var localSha = ComputeGitBlobHash(localPath);
                            if (string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
                            {
                                Log.Debug($"Skipping file {name}, hash matches.");
                                continue;
                            }

                            Log.Debug($"File {name} exists but hash mismatch, re-downloading.");
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Error computing hash for {name}: {ex.Message}");
                        }
                    }

                    var fileBytes = await client.GetByteArrayAsync(downloadUrl);
                    await File.WriteAllBytesAsync(localPath, fileBytes);
                    break;
                }
                case "dir":
                {
                    var subdirUrl = item.GetProperty("url").GetString();
                    if (!Directory.Exists(localPath)) Directory.CreateDirectory(localPath);

                    await DownloadDirectoryRecursive(client, subdirUrl!, localPath);
                    break;
                }
            }
        }
    }
    
    internal static async Task DownloadGitHubDirectoryAsync(string url, string destinationFolder)
    {
        if (string.IsNullOrWhiteSpace(url) || !Directory.Exists(destinationFolder))
            return;

        Log.Debug("Downloading GitHub directory...");

        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 || segments[2] != "tree")
            throw new ArgumentException("URL must be in the format https://github.com/{owner}/{repo}/tree/{branch}/{path}");

        var owner = segments[0];
        var repo = segments[1];
        var branch = segments[3];
        var path = segments.Length > 4 ? string.Join("/", segments.Skip(4)) : string.Empty;

        var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}?ref={branch}";
        Log.Debug($"Downloading directory from GitHub API: {apiUrl}");

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        var token = await TokenManager.RetrieveTokenAsync("GitHub");
        if (!Validator.IsNullOrWhiteSpace(token, logLevel:"debug"))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);

        await DownloadDirectoryRecursive(httpClient, apiUrl, destinationFolder);
    }

    public static async Task<bool> CommitAndPushFile(string[] relativeFilePaths, string commitMessage)
    {
        if (!Validator.DirectoryExists(GetTempRepoPath()) || !Repository.IsValid(GetTempRepoPath()))
        {
            Log.Error($"Error: {GetTempRepoPath()} is not a valid Git repository.");
            return false;
        }

        return await Task.Run(async () =>
        {
            try
            {
                using var repo = new Repository(GetTempRepoPath());
                var hasChanges = false;

                foreach (var path in relativeFilePaths)
                {
                    var fullFilePath = Path.Combine(GetTempRepoPath(), path);

                    if (!File.Exists(fullFilePath))
                    {
                        Log.Error($"Error: File {fullFilePath} does not exist");
                        continue;
                    }

                    Commands.Stage(repo, fullFilePath);
                    hasChanges = true;
                }

                if (!hasChanges)
                {
                    Log.Warning("No changes to commit.");
                    return false;
                }

                var signature = new Signature("PackForge", "packforge@satherov.dev", DateTimeOffset.Now);
                var commit = repo.Commit(commitMessage, signature, signature);

                Log.Information($"Committed Automatic Updates: {commit.Sha}");

                var remote = repo.Network.Remotes["origin"];

                var token = await TokenManager.RetrieveTokenAsync("GitHub");
                if (Validator.IsNullOrWhiteSpace(token, logLevel:"debug")) return false;
                
                var options = new PushOptions
                {
                    CredentialsProvider = (_, _, _) =>
                        new UsernamePasswordCredentials
                        {
                            Username = "PackForge",
                            Password = token
                        }
                };

                try
                {
                    repo.Network.Push(remote, @"refs/heads/main", options);
                    Log.Information("Pushed changes successfully.");
                    return true;
                }
                catch (LibGit2SharpException ex)
                {
                    Log.Error($"Git push failed: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error pushing files: {ex.Message}");
                return false;
            }
        });
    }

    public static async Task DeleteTempRepo(bool silent = false)
    {
        if(silent) Log.Debug($"Deleting temporary repository folder: {GetTempPath()}");
        else Log.Information($"Deleting temporary repository folder: {GetTempPath()}");

        if (!Validator.DirectoryExists(GetTempPath()))
        {
            if(silent) Log.Debug($"Temp repo does not exist");
            else Log.Warning($"Temp repo does not exist");
            return;
        }
        
        Log.Debug("Removing read-only attribute");
        new DirectoryInfo(GetTempPath()).Attributes &= ~FileAttributes.ReadOnly;

        try
        {
            await Task.Run(() =>
            {
                foreach (var file in new DirectoryInfo(GetTempPath()).GetFiles("*", SearchOption.AllDirectories))
                    file.Attributes &= ~FileAttributes.ReadOnly;

                Directory.Delete(GetTempPath(), true);
                if(silent) Log.Debug($"Successfully deleted temporary repository folder");
                else Log.Information($"Successfully deleted temporary repository folder");
            });
        }
        catch (Exception ex)
        {
            if(silent) Log.Debug($"Failed to delete temporary repository folder: {ex.Message}");
            Log.Error($"Failed to delete temporary repository folder: {ex.Message}");
        }
    }

    public static async Task<bool> IsValidGitHubRepoAsync(string url, bool silent = false)
    {
        if(silent) Log.Debug($"Validating GitHub repository: {url}");
        else Log.Information($"Validating GitHub repository: {url}");

        try
        {
            var match = GitHubRepo().Match(url);
            if (!match.Success)
            {
                Log.Debug($"GitHub url '{url}' did not match pattern");
                return false;
            }

            var owner = match.Groups[1].Value;
            var repo = match.Groups[2].Value;
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            var token = await TokenManager.RetrieveTokenAsync("GitHub");
            if (!Validator.IsNullOrWhiteSpace(token, logLevel:"debug"))
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);

            var response = await httpClient.GetAsync(apiUrl);
            Log.Debug($"GitHub API returned {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    
    private static string ComputeGitBlobHash(string filePath)
    {
        var fileBytes = File.ReadAllBytes(filePath);
        var header = $"blob {fileBytes.Length}\0";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var combined = headerBytes.Concat(fileBytes).ToArray();
        var hashBytes = SHA1.HashData(combined);
        return Convert.ToHexStringLower(hashBytes);
    }
    
    public static string GetTempRepoPath() => TempRepoPath;
    private static string GetTempPath() => TempRepoPath;

    [GeneratedRegex(@"^https:\/\/github\.com\/([^\/]+)\/([^\/]+)?$")]
    private static partial Regex GitHubRepo();
}
