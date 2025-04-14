using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using PackForge.Core.Data;
using PackForge.Core.Util;
using Serilog;
using Serilog.Events;

namespace PackForge.Core.Service;

public static partial class GitService
{
    public static readonly string TempRepoPath = Path.Combine(Path.GetTempPath(), "PackForge", "TempRepo");

    public record GitHubRepoInfo
    {
        public string Owner { get; set; } = string.Empty;
        public string RepoName { get; set; } = string.Empty;
        public string? DefaultBranch { get; set; }
        public string Url { get; set; } = string.Empty;

        public static GitHubRepoInfo Empty => new()
        {
            Owner = string.Empty,
            RepoName = string.Empty,
            DefaultBranch = string.Empty,
            Url = string.Empty
        };
    }

    public record GitHubUserInfo(string Username, string Email)
    {
        public static GitHubUserInfo Empty => new(string.Empty, string.Empty);
    };

    public static async Task<GitHubRepoInfo> GetGitHubRepoInfoAsync(string url, string? githubToken = null, CancellationToken ct = default)
    {
        if (!TryValidateGitHubUrl(url, out Match? match))
            return GitHubRepoInfo.Empty;

        string owner = match.Groups["owner"].Value;
        string repo = match.Groups["repo"].Value;
        string branchFromUrl = match.Groups["branch"].Value;
        string apiUrl = $"https://api.github.com/repos/{owner}/{repo}";

        JsonDocument? doc = await ExecuteGitHubApiCallAsync(apiUrl, githubToken, ct);
        if (Validator.IsNullOrEmpty(doc)) return GitHubRepoInfo.Empty;

        string? defaultBranch = !string.IsNullOrWhiteSpace(branchFromUrl) ? branchFromUrl : doc.RootElement.GetProperty("default_branch").GetString();

        return new GitHubRepoInfo
        {
            Owner = owner,
            RepoName = repo,
            DefaultBranch = defaultBranch,
            Url = doc.RootElement.GetProperty("html_url").GetString() ?? url
        };
    }

    public static async Task<GitHubUserInfo> GetUserInfoAsync(string githubToken, CancellationToken ct = default)
    {
        if (Validator.IsNullOrWhiteSpace(githubToken))
            return GitHubUserInfo.Empty;

        JsonDocument? doc = await ExecuteGitHubApiCallAsync("https://api.github.com/user", githubToken, ct);
        if (Validator.IsNullOrEmpty(doc)) return GitHubUserInfo.Empty;

        string username = doc.RootElement.GetProperty("login").GetString() ?? string.Empty;
        if (Validator.IsNullOrWhiteSpace(username)) return GitHubUserInfo.Empty;

        string email = $"{username}@users.noreply.github.com";
        return new GitHubUserInfo(username, email);
    }

    public static async Task DownloadOrUpdateRepoAsync(string url, CancellationToken ct = default)
    {
        Log.Information($"Downloading repo from {url}");

        string githubToken = await TokenManager.RetrieveTokenValueByTypeAsync(TokenType.GitHub);
        if (Validator.IsNullOrWhiteSpace(githubToken))
            Log.Warning("GitHub token is not set");

        GitHubRepoInfo repoInfo = await GetGitHubRepoInfoAsync(url, githubToken, ct);
        if (Validator.IsNullOrWhiteSpace(repoInfo.Owner) || Validator.IsNullOrWhiteSpace(repoInfo.RepoName))
            return;

        if (Validator.DirectoryExists(TempRepoPath))
        {
            if (Repository.IsValid(TempRepoPath))
            {
                using Repository repo = new(TempRepoPath);
                string? remoteUrl = repo.Network.Remotes["origin"]?.Url;

                if (string.Equals(repoInfo.Url, remoteUrl, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Updating existing repository");
                    FetchOptions fetchOptions = new()
                    {
                        CredentialsProvider = (_, __, ___) => new UsernamePasswordCredentials { Username = "x-access-token", Password = githubToken }
                    };

                    try
                    {
                        await Task.Run(() =>
                        {
                            Commands.Fetch(repo, "origin", Array.Empty<string>(), fetchOptions, null);
                            Branch? targetBranch = repo.Branches[$"origin/{repoInfo.DefaultBranch}"];

                            Branch localBranch = repo.Branches[repoInfo.DefaultBranch];
                            if (localBranch == null && targetBranch != null)
                            {
                                localBranch = repo.CreateBranch(repoInfo.DefaultBranch, targetBranch.Tip);
                                repo.Branches.Update(localBranch, b => b.TrackedBranch = targetBranch.CanonicalName);
                            }

                            if (localBranch == null) return;
                            Commands.Checkout(repo, localBranch);
                            repo.Reset(ResetMode.Hard, localBranch.Tip);
                        }, ct);

                        Log.Information("Repository updated successfully");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to update repository: {ex.Message}");
                    }

                    return;
                }

                Log.Information("Repository URL mismatch. Deleting local folder.");
                await Task.Run(DeleteTempRepo, ct);
            }
            else
            {
                Log.Information("Local folder exists but is not a valid Git repository. Deleting folder.");
                await Task.Run(DeleteTempRepo, ct);
            }
        }

        Log.Information("Cloning repository");
        CloneOptions cloneOptions = new()
        {
            BranchName = repoInfo.DefaultBranch,
            FetchOptions =
            {
                CredentialsProvider = (_, __, ___) => new UsernamePasswordCredentials { Username = "x-access-token", Password = githubToken }
            }
        };

        try
        {
            await Task.Run(() => Repository.Clone(repoInfo.Url, TempRepoPath, cloneOptions), ct);
            Log.Information("Repository cloned successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to clone repository: {ex.Message}");
        }
    }

    private static void DeleteTempRepo()
    {
        if (!Directory.Exists(TempRepoPath)) return;

        DirectoryInfo dirInfo = new(TempRepoPath);
        foreach (FileInfo file in dirInfo.GetFiles("*", SearchOption.AllDirectories)) file.Attributes = FileAttributes.Normal;
        Directory.Delete(TempRepoPath, true);
    }

    public static async Task<bool> CommitFilesAsync(string token, IEnumerable<string> fileRelativePaths, string commitMessage, CancellationToken ct = default)
    {
        using Repository repo = new(TempRepoPath);

        foreach (string relativePath in fileRelativePaths)
        {
            string fullPath = Path.Combine(TempRepoPath, relativePath);
            if (Validator.FileExists(fullPath, LogEventLevel.Debug))
            {
                Commands.Stage(repo, fullPath);
                Log.Debug($"Staging file: {relativePath}");
            }
            else
            {
                Log.Warning($"File not found: {fullPath}");
            }
        }

        if (repo.Index.Count == 0)
        {
            Log.Information("No files staged for commit.");
            return false;
        }

        IEnumerable<string> filesToCommit;
        if (repo.Head.Tip != null)
        {
            TreeChanges changes = repo.Diff.Compare<TreeChanges>(repo.Head.Tip.Tree, DiffTargets.Index);
            filesToCommit = changes.Select(change => change.Path);
        }
        else
        {
            filesToCommit = repo.Index.Select(entry => entry.Path);
        }

        foreach (string file in filesToCommit) Log.Debug($"File to commit: {file}");


        GitHubUserInfo userInfo = await GetUserInfoAsync(token, ct);

        if (Validator.IsNullOrWhiteSpace(userInfo.Username) || Validator.IsNullOrWhiteSpace(userInfo.Email))
        {
            Log.Warning("User information is not available");
            return false;
        }

        Signature signature = new("Packforge-Automation", "PackForge@satherov.dev", DateTimeOffset.Now);

        StringBuilder commitBuilder = new();
        commitBuilder.AppendLine(commitMessage);
        commitBuilder.AppendLine();
        commitBuilder.AppendLine($"Co-authored-by: {userInfo.Username} <{userInfo.Email}>");

        try
        {
            repo.Commit(commitBuilder.ToString(), signature, signature);
        }
        catch (EmptyCommitException ex)
        {
            Log.Warning($"No changes - nothing to commit");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to commit changes: {ex.Message}");
            return false;
        }

        Log.Information("Files committed successfully.");
        return true;
    }

    public static async Task PushCommits(string token, string url, CancellationToken ct = default)
    {
        if (!Repository.IsValid(TempRepoPath))
        {
            Log.Warning("Push skipped: invalid Git repository.");
            return;
        }

        using Repository repo = new(TempRepoPath);

        if (repo.Head?.Tip == null)
        {
            Log.Information("Push skipped: no commits on current branch.");
            return;
        }

        GitHubRepoInfo repoInfo = await GetGitHubRepoInfoAsync(url, token, ct);
        Branch localBranch = repo.Branches[repoInfo.DefaultBranch] ?? repo.CreateBranch(repoInfo.DefaultBranch, repo.Head.Tip);

        string remoteName = localBranch.TrackedBranch?.RemoteName ?? "origin";
        if (repo.Network.Remotes.Any(r => r.Name == remoteName)) repo.Network.Remotes.Remove(remoteName);
        repo.Network.Remotes.Add(remoteName, url);

        Branch? remoteBranch = repo.Branches[$"{remoteName}/{localBranch.FriendlyName}"];
        bool hasNewCommits = false;

        if (remoteBranch != null)
        {
            HistoryDivergence? divergence = repo.ObjectDatabase.CalculateHistoryDivergence(localBranch.Tip, remoteBranch.Tip);
            hasNewCommits = divergence?.AheadBy > 0;
        }
        else
        {
            hasNewCommits = true;
        }

        if (!hasNewCommits)
        {
            Log.Information("Push skipped: branch is up-to-date with remote.");
            return;
        }

        IEnumerable<Commit> newCommits = remoteBranch != null
            ? repo.Commits.QueryBy(new CommitFilter
            {
                IncludeReachableFrom = localBranch.Tip,
                ExcludeReachableFrom = remoteBranch.Tip
            }).ToList()
            : [];

        if (!newCommits.Any())
        {
            Log.Information("Push skipped: no new commits to push.");
            return;
        }

        foreach (Commit commit in newCommits)
            Log.Debug($"Pushing commit: {commit.MessageShort} - {commit.Sha}");

        PushOptions pushOptions = new()
        {
            CredentialsProvider = (_, _, _) => new UsernamePasswordCredentials
            {
                Username = "x-access-token",
                Password = token
            }
        };

        Remote remote = repo.Network.Remotes[remoteName];
        string pushRefSpec = $"refs/heads/{localBranch.FriendlyName}:refs/heads/{localBranch.FriendlyName}";
        repo.Network.Push(remote, pushRefSpec, pushOptions);

        Log.Information($"Branch '{localBranch.FriendlyName}' pushed successfully to remote with URL '{url}'");
    }


    private static async Task<JsonDocument?> ExecuteGitHubApiCallAsync(string apiUrl, string? githubToken = null, CancellationToken ct = default)
    {
        using HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PackForgeApp");
        if (!Validator.IsNullOrWhiteSpace(githubToken, LogEventLevel.Debug))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", githubToken);

        HttpResponseMessage response = await client.GetAsync(apiUrl, ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Error($"GitHub API call to {apiUrl} failed with status code: {response.StatusCode}");
            return null;
        }

        string json = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json);
    }

    public static bool TryValidateGitHubUrl(string url, [NotNullWhen(true)] out Match? match)
    {
        if (Validator.IsNullOrEmpty(url))
        {
            match = null;
            return false;
        }

        match = GitHubRepoUrl().Match(url);
        if (match.Success)
            return true;

        Log.Warning("Invalid GitHub URL provided");
        return false;
    }

    [GeneratedRegex(@"^https?:\/\/github\.com\/(?<owner>[^\/]+)\/(?<repo>[^\/]+)(?:\/tree\/(?<branch>[^\/\s]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubRepoUrl();
}