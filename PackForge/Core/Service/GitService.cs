using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using PackForge.Core.Helpers;
using Serilog;
using Signature = Tmds.DBus.Protocol.Signature;

namespace PackForge.Core.Service;

public static partial class GitService
{
    private static readonly string TempRepoPath;
    private static readonly string TempPath;
    
    static GitService()
    {
        var path = Path.GetTempPath();
        TempRepoPath = Path.Combine(path, "PackForge", "TempRepo");
        TempPath = path;
    }
    
    public static async Task<string> SilentCloneGitHubRepo(string? gitHubUrl, CancellationToken cts)
    {
        Log.Debug("Starting silent clone task...");
        if (!Validator.IsNullOrWhiteSpace(gitHubUrl) && await IsValidGitHubRepoAsync(gitHubUrl!, true)) return await CloneGitHubRepo(gitHubUrl!, cts, true);
        Log.Debug("Invalid GitHub repository URL provided to silent task");
        return string.Empty;
    }
    
    /// <summary>
    /// Clones the GitHub repository to a temporary folder for further use
    /// </summary>
    /// <param name="gitHubUrl">Url to clone from</param>
    /// <param name="userCts">CancellationToken used for user requested process killing</param>
    /// <param name="silent">Defines the log level for downloads. false = default, true = debug</param>
    /// <param name="timeoutMilliseconds">Timeout to prevent deadlock. Defaults to 60 seconds</param>
    /// <returns></returns>
    public static async Task<string> CloneGitHubRepo(string gitHubUrl, CancellationToken userCts, bool silent = false, int timeoutMilliseconds = 60000)
    {
        if(silent) Log.Debug($"Downloading GitHub repository: {gitHubUrl}");
        else Log.Information($"Downloading GitHub repository: {gitHubUrl}");
        
        using var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, userCts);

        if (Validator.DirectoryExists(GetTempPath()))
        {
            Log.Debug("Deleting existing temp repo");
            await DeleteTempRepo(silent);
        }
        Log.Debug("Creating new temp repo");
        Directory.CreateDirectory(GetTempRepoPath());

        // Extract owner and repo from URL
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
            Log.Debug($"Requesting default branch from GitHub API");
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
            try
            {
                var response = await httpClient.GetAsync(apiUrl, linkedCts.Token);
                if (response.IsSuccessStatusCode)
                {
                    Log.Debug($"GitHub API returned {response.StatusCode}");
                    var json = await response.Content.ReadAsStringAsync(linkedCts.Token);
                    using var jsonDoc = JsonDocument.Parse(json);
                    if (jsonDoc.RootElement.TryGetProperty("default_branch", out var branchElement))
                        defaultBranch = branchElement.GetString() ?? defaultBranch;
                    Log.Debug($"Returned default branch: {defaultBranch}");
                }
                else
                {
                    if(silent) Log.Debug($"GitHub API returned {response.StatusCode}. Using '{defaultBranch}'");
                    else Log.Warning($"GitHub API returned {response.StatusCode}. Using '{defaultBranch}'");
                }
            }
            catch (Exception ex)
            {
                if(silent) Log.Debug($"API error: {ex.Message}. Using '{defaultBranch}'");
                else Log.Error($"API error: {ex.Message}. Using '{defaultBranch}'");
            }
        }

        var cloneOptions = new CloneOptions
        {
            FetchOptions =
            {
                Depth = 1
            },
            BranchName = defaultBranch
        };

        try
        {
            await Task.Run(() =>
            {
                Repository.Clone(gitHubUrl, GetTempRepoPath(), cloneOptions);
            }, linkedCts.Token);

            if(silent) Log.Debug($"Successfully downloaded GitHub repository: {gitHubUrl}");
            else Log.Information($"Successfully downloaded GitHub repository: {gitHubUrl}");
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

        return string.Empty;
    }

    /// <summary>
    /// Automatically commits and pushes files to the GitHub repository
    /// </summary>
    /// <param name="relativeFilePaths">Path of all files to commit</param>
    /// <param name="commitMessage">Message used to auto commit</param>
    /// <param name="personalAccessToken">Access token of the user</param>
    /// <returns></returns>
    public static async Task<bool> CommitAndPushFile(string[] relativeFilePaths, string commitMessage, string personalAccessToken)
    {
        if (!Validator.DirectoryExists(GetTempRepoPath()) || !Repository.IsValid(GetTempRepoPath()))
        {
            Log.Error($"Error: {GetTempRepoPath()} is not a valid Git repository.");
            return false;
        }

        return await Task.Run(() =>
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

    /// <summary>
    /// Checks if the provided URL is a valid GitHub repository
    /// </summary>
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

            var response = await httpClient.GetAsync(apiUrl);

            Log.Debug($"GitHub API returned {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    
    public static string GetTempRepoPath() => TempRepoPath;
    public static string GetTempPath() => TempRepoPath;

    [GeneratedRegex(@"^https:\/\/github\.com\/([^\/]+)\/([^\/]+)?$")]
    private static partial Regex GitHubRepo();
}