using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Serilog;
using Signature = Tmds.DBus.Protocol.Signature;

namespace PackForge.Core.Service;

public static partial class GitService
{
    private static readonly string TempRepoPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    /// <summary>
    /// Clones the GitHub repository to a temporary folder for further use
    /// </summary>
    /// <param name="gitHubUrl">Url to clone from</param>
    /// <param name="userCts">CancellationToken used for user requested process killing</param>
    /// <param name="timeoutMilliseconds">Timeout to prevent deadlock. Defaults to 60 seconds</param>
    /// <returns></returns>
    public static async Task<string?> CloneGitHubRepo(string gitHubUrl, CancellationToken userCts, int timeoutMilliseconds = 60000)
{
    using var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, userCts);

    // Prepare destination folder
    if (Directory.Exists(TempRepoPath))
        Directory.Delete(TempRepoPath, true);
    Directory.CreateDirectory(TempRepoPath);

    // Extract owner and repo from URL
    var match = GitHubRepo().Match(gitHubUrl);
    if (!match.Success)
    {
        Log.Error("Invalid GitHub URL.");
        return null;
    }
    var owner = match.Groups[1].Value;
    var repo = match.Groups[2].Value;

    // Get default branch (fallback to "main")
    var defaultBranch = "main";
    var apiUrl = $"https://api.github.com/repos/{owner}/{repo}";
    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
    try
    {
        var response = await httpClient.GetAsync(apiUrl, linkedCts.Token);
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync(linkedCts.Token);
            using var jsonDoc = JsonDocument.Parse(json);
            if (jsonDoc.RootElement.TryGetProperty("default_branch", out var branchElement))
                defaultBranch = branchElement.GetString() ?? defaultBranch;
        }
        else
        {
            Log.Warning($"GitHub API returned {response.StatusCode}. Using '{defaultBranch}'.");
        }
    }
    catch (Exception ex)
    {
        Log.Error($"API error: {ex.Message}. Using '{defaultBranch}'.");
    }

    // Download repository as a zip
    var zipUrl = $"https://github.com/{owner}/{repo}/archive/refs/heads/{defaultBranch}.zip";
    Log.Information($"Downloading {zipUrl}");
    var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");

    try
    {
        var response = await httpClient.GetAsync(zipUrl, linkedCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            Log.Error($"Download failed: {response.StatusCode}");
            return null;
        }
        await using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write)) await response.Content.CopyToAsync(fs, linkedCts.Token);

        // Extract and flatten if zip contains a single folder
        System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, TempRepoPath);
        var directories = Directory.GetDirectories(TempRepoPath);
        if (directories.Length != 1) return TempRepoPath;
        
        var innerDir = directories[0];
        foreach (var dir in Directory.GetDirectories(innerDir, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(dir.Replace(innerDir, TempRepoPath));
        foreach (var file in Directory.GetFiles(innerDir, "*.*", SearchOption.AllDirectories)) File.Move(file, file.Replace(innerDir, TempRepoPath));
        Directory.Delete(innerDir, true);
        
        return TempRepoPath;
    }
    catch (OperationCanceledException)
    {
        Log.Warning("Operation canceled");
    }
    catch (Exception ex)
    {
        Log.Error($"Error: {ex.Message}");
    }
    finally
    {
        if (File.Exists(tempZipPath))
            File.Delete(tempZipPath);
    }
    return null;
}

    /// <summary>
    /// Automatically commits and pushes files to the GitHub repository
    /// </summary>
    /// <param name="relativeFilePaths">Path of all files to commit</param>
    /// <param name="commitMessage">Message used to auto commit</param>
    /// <param name="personalAccessToken">Access token of the user</param>
    /// <returns></returns>
    public static async Task<bool> CommitAndPushFile(string[] relativeFilePaths, string commitMessage,
        string personalAccessToken)
    {
        if (!Directory.Exists(TempRepoPath) || !Repository.IsValid(TempRepoPath))
        {
            Log.Error($"Error: {TempRepoPath} is not a valid Git repository.");
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(TempRepoPath);

                var hasChanges = false;

                foreach (var path in relativeFilePaths)
                {
                    var fullFilePath = Path.Combine(TempRepoPath, path);

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

                var signature = new LibGit2Sharp.Signature("PackForge", "packforge@allthemods.net", DateTimeOffset.Now);
                var commit = repo.Commit(commitMessage, signature, signature);

                Log.Information($"Committed Automatic Updates: {commit.Sha}");

                var remote = repo.Network.Remotes["origin"];
                var options = new PushOptions
                {
                    CredentialsProvider = (_, _, _) =>
                        new UsernamePasswordCredentials
                        {
                            Username = "PackForge",
                            Password = personalAccessToken
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

    /// <summary>
    /// Deletes the temporary repository folder to prevent clutter
    /// </summary>
    /// <param name="folderPath">Path to delete</param>
    public static async Task DeleteTempRepo(string? folderPath)
    {
        Log.Information($"Deleting temporary repository folder: {folderPath}");

        if (!Directory.Exists(folderPath))
        {
            Log.Warning($"Temp repo does not exist");
            return;
        }
        
        Log.Debug("Removing read-only attribute");
        new DirectoryInfo(TempRepoPath).Attributes &= ~FileAttributes.ReadOnly;

        try
        {
            await Task.Run(() =>
            {
                foreach (var file in new DirectoryInfo(folderPath).GetFiles("*", SearchOption.AllDirectories))
                    file.Attributes &= ~FileAttributes.ReadOnly;

                Directory.Delete(folderPath, true);
                Log.Information($"Successfully deleted temporary repository folder");
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to delete temporary repository folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the provided URL is a valid GitHub repository
    /// </summary>
    public static async Task<bool> IsValidGitHubRepoAsync(string url)
    {
        Log.Information($"Validating GitHub repository: {url}");

        try
        {
            var match = GitHubRepo().Match(url);
            if (!match.Success)
            {
                Log.Debug($"GitHub url {url} did not match pattern");
                return false;
            }

            var owner = match.Groups[1].Value;
            var repo = match.Groups[2].Value;

            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

            var response = await httpClient.GetAsync(apiUrl);

            Log.Debug($"GitHub API returned {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex(@"^https:\/\/github\.com\/([^\/]+)\/([^\/]+)?$")]
    private static partial Regex GitHubRepo();
}