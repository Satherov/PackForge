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
    public static readonly string GitRepoPath = Path.Combine(App.AppDataPath, "github");
    
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
    
    public static async Task CloneOrUpdateRepoAsync(string url, CancellationToken ct = default)
    {
        if (Directory.Exists(GitRepoPath) && Repository.IsValid(GitRepoPath))
            await UpdateRepoAsync(url, ct);
        else
            await CloneRepoAsync(url, ct);
    }

    public static async Task CloneRepoAsync(string url, CancellationToken ct = default)
    {
        Log.Information("Cloning repository");

        string githubToken = await TokenManager.RetrieveTokenValueByTypeAsync(TokenType.GitHub);
        if (string.IsNullOrWhiteSpace(githubToken))
            Log.Warning("GitHub token is not set");

        GitHubRepoInfo repoInfo = await GetGitHubRepoInfoAsync(url, githubToken, ct);
        if (string.IsNullOrWhiteSpace(repoInfo.Owner) || string.IsNullOrWhiteSpace(repoInfo.RepoName))
            return;

        if (Directory.Exists(GitRepoPath))
            await Task.Run(DeleteTempRepo, ct);

        CloneOptions cloneOptions = new()
        {
            BranchName = repoInfo.DefaultBranch,
            FetchOptions = {
                CredentialsProvider = (_,_,_) => new UsernamePasswordCredentials {
                    Username = "x-access-token", Password = githubToken }
            }
        };

        try
        {
            await Task.Run(() => Repository.Clone(repoInfo.Url, GitRepoPath, cloneOptions), ct);
            Log.Information("Repository cloned successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to clone repository: {ex.Message}");
        }
    }

    public static async Task UpdateRepoAsync(string url, CancellationToken ct = default)
    {
        Log.Information("Updating existing repository");

        string githubToken = await TokenManager.RetrieveTokenValueByTypeAsync(TokenType.GitHub);
        if (string.IsNullOrWhiteSpace(githubToken))
            Log.Warning("GitHub token is not set");

        GitHubRepoInfo repoInfo = await GetGitHubRepoInfoAsync(url, githubToken, ct);
        if (!Repository.IsValid(GitRepoPath))
        {
            Log.Warning("Temp path is not a valid repo; falling back to clone");
            await CloneRepoAsync(url, ct);
            return;
        }

        using Repository repo = new(GitRepoPath);
        Remote? remote = repo.Network.Remotes["origin"];
        if (!string.Equals(remote?.Url, repoInfo.Url, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Remote URL mismatch; falling back to clone");
            await CloneRepoAsync(url, ct);
            return;
        }

        FetchOptions fetchOpts = new()
        {
            CredentialsProvider = (_,_,_) =>
                new UsernamePasswordCredentials { Username = "x-access-token", Password = githubToken }
        };

        await Task.Run(() => Commands.Fetch(repo, "origin", Array.Empty<string>(), fetchOpts, null), ct);
        Branch? remoteBranch = repo.Branches[$"origin/{repoInfo.DefaultBranch}"];
        Branch? localBranch = repo.Branches[repoInfo.DefaultBranch]
                              ?? repo.CreateBranch(repoInfo.DefaultBranch, remoteBranch.Tip);

        repo.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
        Commands.Checkout(repo, localBranch);
        repo.Reset(ResetMode.Hard, remoteBranch.Tip);
        repo.Merge(remoteBranch, repo.Config.BuildSignature(DateTimeOffset.Now), new MergeOptions());

        Log.Information("Repository updated successfully");
    }
    
    public static async Task GetRepoStatusAsync(CancellationToken ct = default)
    {
        if (!Repository.IsValid(GitRepoPath))
        {
            Log.Warning($"No valid Git repository at {GitRepoPath}");
            return;
        }

        await Task.Run(() =>
        {
            using Repository repo = new(GitRepoPath);
            RepositoryStatus? status = repo.RetrieveStatus(new StatusOptions());

            if (!status.Any())
            {
                Log.Information("Working directory clean");
                return;
            }

            // Staged (index) changes
            foreach (StatusEntry? e in status.Where(e =>
                         e.State.HasFlag(FileStatus.NewInIndex)      ||
                         e.State.HasFlag(FileStatus.ModifiedInIndex) ||
                         e.State.HasFlag(FileStatus.DeletedFromIndex)||
                         e.State.HasFlag(FileStatus.RenamedInIndex)  ||
                         e.State.HasFlag(FileStatus.TypeChangeInIndex)))
            {
                Log.Information($"Staged:   {e.FilePath} ({e.State})");
            }

            // Unstaged (workdir) changes
            foreach (StatusEntry? e in status.Where(e =>
                         e.State.HasFlag(FileStatus.NewInWorkdir)       ||
                         e.State.HasFlag(FileStatus.ModifiedInWorkdir)  ||
                         e.State.HasFlag(FileStatus.DeletedFromWorkdir) ||
                         e.State.HasFlag(FileStatus.RenamedInWorkdir)   ||
                         e.State.HasFlag(FileStatus.TypeChangeInWorkdir)))
            {
                Log.Information($"Modified: {e.FilePath} ({e.State})");
            }

            // Untracked files
            foreach (StatusEntry? e in status.Where(e => e.State == FileStatus.NewInWorkdir && e.State.HasFlag(FileStatus.Ignored) == false))
            {
                Log.Information($"Untracked:{e.FilePath}");
            }

            // Ignored files
            foreach (StatusEntry? e in status.Where(e => e.State.HasFlag(FileStatus.Ignored)))
            {
                Log.Information($"Ignored:  {e.FilePath}");
            }

            // Conflicts
            foreach (StatusEntry? e in status.Where(e => e.State.HasFlag(FileStatus.Conflicted)))
            {
                Log.Information($"Conflict: {e.FilePath}");
            }
        }, ct);
    }
    
    public static async Task<int> StageAsync(string path, CancellationToken ct = default)
    {
        if (Validator.IsNullOrWhiteSpace(path)) return 0;
        
        if (!Repository.IsValid(GitRepoPath))
        {
            Log.Warning($"Cannot stage files: no valid repo at {GitRepoPath}");
            return 0;
        }

        if (path.StartsWith(GitRepoPath, StringComparison.OrdinalIgnoreCase))
        {
            path = Path.GetRelativePath(GitRepoPath, path);
        }

        using Repository repo = new(GitRepoPath);
        await Task.Run(() =>
        {
            string fullPath = Path.Combine(GitRepoPath, path);

            if (Directory.Exists(fullPath))
            {
                string[] allFiles = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
                if (allFiles.Length > 0)
                {
                    Commands.Stage(repo, allFiles);
                    Log.Debug($"Staged directory '{path}' ({allFiles.Length} files)");
                    return 1;
                }

                Log.Warning($"Directory '{path}' is empty");
                return 0;
            }

            if (File.Exists(fullPath))
            {
                Commands.Stage(repo, fullPath);
                Log.Debug($"Staged file '{path}'");
                return 1;
            }

            Log.Warning($"Path not found: '{path}'");
            return 0;
        }, ct);
        
        return 1;
    }

    private static void DeleteTempRepo()
    {
        if (!Directory.Exists(GitRepoPath)) return;

        DirectoryInfo dirInfo = new(GitRepoPath);
        foreach (FileInfo file in dirInfo.GetFiles("*", SearchOption.AllDirectories)) file.Attributes = FileAttributes.Normal;
        Directory.Delete(GitRepoPath, true);
    }

    public static async Task<bool> CommitAsync(string token, string commitMessage, CancellationToken ct = default)
    {
        if(Validator.IsNullOrWhiteSpace(token) || Validator.IsNullOrWhiteSpace(commitMessage)) return false;
        
        using Repository repo = new(GitRepoPath);

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

    public static async Task PushAsync(string token, string url, CancellationToken ct = default)
    {
        if(Validator.IsNullOrWhiteSpace(token) || Validator.IsNullOrWhiteSpace(url)) return;
        
        if (!Repository.IsValid(GitRepoPath))
        {
            Log.Warning("Push skipped: invalid Git repository.");
            return;
        }

        using Repository repo = new(GitRepoPath);

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