using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PackForge.Core;
using PackForge.Core.Builders;
using PackForge.Core.Data;
using PackForge.Core.Service;
using PackForge.Core.Terminal;
using PackForge.Core.Util;
using PackForge.Logger;
using Serilog;
using Serilog.Events;
using static System.Int32;

namespace PackForge.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static Task _silentTask = Task.CompletedTask;
    public static CancellationTokenSource Cts = new();

    private static readonly ConcurrentQueue<GlobalLog.LogEntry> LogQueue = new();
    private static CancellationTokenSource _logCts = new();

    private static readonly string TemplateFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PackForge", "templates");

    private static readonly ObservableCollection<GlobalLog.LogEntry> LogEntries = GlobalLog.LogEntries;
    private static readonly ObservableCollection<GlobalLog.LogEntry> PrivateLogEntries = [];
    public static ReadOnlyObservableCollection<GlobalLog.LogEntry> FilteredLogEntries { get; } = new(PrivateLogEntries);

    private static bool _showDebugLogs;

    public static bool ShowDebugLogs
    {
        get => _showDebugLogs;
        set
        {
            if (_showDebugLogs == value)
                return;
            _showDebugLogs = value;
            RefreshFilteredLogEntries();
        }
    }

    private static void RefreshFilteredLogEntries()
    {
        IEnumerable<GlobalLog.LogEntry> filtered = ShowDebugLogs ? LogEntries : LogEntries.Where(e => e.Level != LogEventLevel.Debug);

        PrivateLogEntries.Clear();
        foreach (GlobalLog.LogEntry entry in filtered)
            PrivateLogEntries.Add(entry);
    }

    private static void InitLogs()
    {
        GlobalLog.LogEntries.CollectionChanged += (_, args) =>
        {
            if (args.NewItems == null) return;

            foreach (object? item in args.NewItems)
                if (item is GlobalLog.LogEntry entry)
                    LogQueue.Enqueue(entry);
        };

        _ = Task.Run(() => ProcessLogs(_logCts.Token));
    }

    private static async Task ProcessLogs(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            List<GlobalLog.LogEntry> batch = [];

            while (LogQueue.TryDequeue(out GlobalLog.LogEntry? entry))
            {
                if (_showDebugLogs || entry.Level != LogEventLevel.Debug)
                    batch.Add(entry);

                if (batch.Count >= 50)
                    break;
            }

            if (batch.Count > 0)
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (GlobalLog.LogEntry e in batch)
                        PrivateLogEntries.Add(e);
                });

            await Task.Delay(50, ct);
        }
    }

    public static void Shutdown()
    {
        _logCts.Cancel();
    }

    public ReadOnlyObservableCollection<string> LoaderTypeOptions { get; init; } = new([
        "NeoForge",
        "Forge"
    ]);

    public ReadOnlyObservableCollection<string> MinecraftVersionOptions { get; } = new([
        "1.21.4", "1.21.3", "1.21.1", "1.21",
        "1.20.6", "1.20.4", "1.20.3", "1.20.2", "1.20.1", "1.20",
        "1.19.4", "1.19.3", "1.19.2", "1.19.1", "1.19",
        "1.18.2", "1.18.1", "1.18",
        "1.17.1",
        "1.16.5", "1.16.4", "1.16.3", "1.16.2", "1.16.1", "1.16",
        "1.15.2", "1.15.1", "1.15",
        "1.14.4", "1.14.3", "1.14.2",
        "1.13.2",
        "1.12.2", "1.12.1", "1.12",
        "1.11.2", "1.11",
        "1.10.2", "1.10",
        "1.9.4", "1.9",
        "1.8.9", "1.8.8", "1.8",
        "1.7.10", "1.7.2",
        "1.6.4", "1.6.3", "1.6.2", "1.6.1",
        "1.5.2", "1.5.1", "1.5",
        "1.4.7", "1.4.6", "1.4.5", "1.4.4", "1.4.2", "1.4.0",
        "1.3.2",
        "1.2.5", "1.2.4", "1.2.3",
        "1.1"
    ]);

    public ObservableCollection<string> LoaderVersionOptions { get; set; } = [];

    [ObservableProperty] private string? _sourceFolderPath;
    [ObservableProperty] private string? _destinationFolderPath;
    [ObservableProperty] private string? _gitHubLink;
    [ObservableProperty] private string? _minecraftVersion;
    [ObservableProperty] private string? _loaderType;
    [ObservableProperty] private string? _loaderVersion;
    [ObservableProperty] private string? _recommendedRam;
    [ObservableProperty] private string? _modpackName;
    [ObservableProperty] private string? _modpackVersion;
    [ObservableProperty] private string? _curseforgeId;
    [ObservableProperty] private string? _modpackAuthor;

    public AsyncRelayCommand? KillTasksCommand { get; set; }
    public AsyncRelayCommand? OpenSourceFolderCommand { get; set; }
    public AsyncRelayCommand? OpenDestinationFolderCommand { get; set; }
    public AsyncRelayCommand? OpenGitHubRepoCommand { get; set; }
    public AsyncRelayCommand? FetchLoaderVersionCommand { get; set; }
    public AsyncRelayCommand? GenerateClientCommand { get; set; }
    public AsyncRelayCommand? GenerateServerCommand { get; set; }
    public AsyncRelayCommand? GenerateChangelogCommand { get; set; }
    public AsyncRelayCommand? GenerateAllCommand { get; set; }
    public AsyncRelayCommand? ApplyFiltersCommand { get; set; }
    public AsyncRelayCommand? PushToGitHubCommand { get; set; }
    public AsyncRelayCommand? OpenConfigWindowCommand { get; set; }
    public AsyncRelayCommand? OpenFilterWindowCommand { get; set; }
    public AsyncRelayCommand? OpenTokenWindowCommand { get; set; }
    public AsyncRelayCommand? OpenOverwriteWindowCommand { get; set; }
    public AsyncRelayCommand? OpenTemplateFolderCommand { get; set; }

    public MainWindowViewModel()
    {
        InitLogs();
        LoadData();
        _silentTask = TokenManager.IsTokenStored(TokenType.GitHub) && !Validator.IsNullOrWhiteSpace(GitHubLink, LogEventLevel.Debug)
            ? GitService.CloneOrUpdateRepoAsync(GitHubLink, Cts.Token)
            : Task.CompletedTask;
        InitializeCommands();
    }

    private void InitializeCommands()
    {
        KillTasksCommand = CreateCommand(CancelAllOperations);
        OpenSourceFolderCommand = new AsyncRelayCommand(async () => SourceFolderPath = await Task.Run(() => FileUtil.OpenFolderAsync(SourceFolderPath)));
        OpenDestinationFolderCommand = new AsyncRelayCommand(async () => DestinationFolderPath = await Task.Run(() => FileUtil.OpenFolderAsync(DestinationFolderPath)));
        OpenGitHubRepoCommand = CreateCommand(() => OpenGitHubRepoLink(GitHubLink));
        FetchLoaderVersionCommand = CreateCommand(() => FetchLoaderVersionsAsync(LoaderType, MinecraftVersion));

        GenerateClientCommand = new AsyncRelayCommand(async () => await GenerateClientAsync(SourceFolderPath, DestinationFolderPath, GitHubLink, _silentTask, MinecraftVersion,
            LoaderType, LoaderVersion, ModpackName, ModpackVersion, ModpackAuthor, CurseforgeId, Cts.Token));
        GenerateServerCommand = new AsyncRelayCommand(async () => await GenerateServerAsync(SourceFolderPath, DestinationFolderPath, GitHubLink, _silentTask, LoaderType,
            LoaderVersion, ModpackName, ModpackVersion, CurseforgeId, Cts.Token));
        GenerateChangelogCommand = CreateCommand(() => GenerateChangelog(SourceFolderPath, DestinationFolderPath, ModpackName, ModpackVersion, Cts.Token));
        GenerateAllCommand = CreateCommand(async () =>
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Log.Information("Running all tasks");

            if (!Validator.IsNullOrWhiteSpace(GitHubLink))
                await GenerateRepo(GitHubLink, _silentTask, Cts.Token);

            await Task.WhenAll(
                GenerateClientAsync(SourceFolderPath, DestinationFolderPath, GitHubLink, null, MinecraftVersion, LoaderType, LoaderVersion, ModpackName, ModpackVersion,
                    ModpackAuthor, CurseforgeId, Cts.Token),
                GenerateServerAsync(SourceFolderPath, DestinationFolderPath, GitHubLink, null, LoaderType, LoaderVersion, ModpackName, ModpackVersion, CurseforgeId, Cts.Token),
                GenerateChangelog(SourceFolderPath, DestinationFolderPath, ModpackName, ModpackVersion, Cts.Token));

            Log.Information($"All tasks completed after {stopwatch.ElapsedMilliseconds}ms");
        });


        ApplyFiltersCommand = CreateCommand(async () =>
        {
            if (Validator.DirectoryEmpty(SourceFolderPath)) return;
            await FileUtil.ApplyFilters(Path.Combine(SourceFolderPath, "mods"), true, Cts.Token);
        });

        PushToGitHubCommand = CreateCommand(() => PushToGitHub(GitHubLink, _silentTask, Cts.Token));

        OpenConfigWindowCommand = CreateCommand(() => Task.Run(() => WindowHelper.ShowWindow(() => WindowHelper.ConfigWindow)));
        OpenFilterWindowCommand = CreateCommand(() => Task.Run(() => WindowHelper.ShowWindow(() => WindowHelper.FilterWindow)));
        OpenTokenWindowCommand = CreateCommand(() => Task.Run(() => WindowHelper.ShowWindow(() => WindowHelper.TokenWindow)));
        OpenOverwriteWindowCommand = CreateCommand(() => Task.Run(() => WindowHelper.ShowWindow(() => WindowHelper.OverwriteWindow)));
        OpenTemplateFolderCommand = CreateCommand(() => Task.Run(() =>
        {
            if (!Directory.Exists(TemplateFolderPath))
                Directory.CreateDirectory(TemplateFolderPath);

            Process.Start(new ProcessStartInfo
            {
                FileName = TemplateFolderPath,
                UseShellExecute = true
            });
        }));
    }

    private static AsyncRelayCommand CreateCommand(Func<Task> action)
    {
        return new AsyncRelayCommand(async () => await Task.Run(action));
    }

    private void LoadData()
    {
        DataManager.LoadConfig();

        SourceFolderPath = DataManager.GetConfigValue(() => DataManager.SourceFolderPath);
        DestinationFolderPath = DataManager.GetConfigValue(() => DataManager.DestinationFolderPath);
        GitHubLink = DataManager.GetConfigValue(() => DataManager.GitHubLink);
        MinecraftVersion = DataManager.GetConfigValue(() => DataManager.MinecraftVersion);
        LoaderType = DataManager.GetConfigValue(() => DataManager.LoaderType);
        LoaderVersion = DataManager.GetConfigValue(() => DataManager.LoaderVersion);
        LoaderVersionOptions = !Validator.IsNullOrWhiteSpace(LoaderVersion, null) ? new ObservableCollection<string>([LoaderVersion]) : [];
        RecommendedRam = DataManager.GetConfigValue(() => DataManager.RecommendedRam);
        ModpackName = DataManager.GetConfigValue(() => DataManager.ModpackName);
        ModpackVersion = DataManager.GetConfigValue(() => DataManager.ModpackVersion);
        CurseforgeId = DataManager.GetConfigValue(() => DataManager.CurseforgeId);
        ModpackAuthor = DataManager.GetConfigValue(() => DataManager.ModpackAuthor);
    }

    public void SaveData()
    {
        DataManager.SetConfigValue(() => DataManager.SourceFolderPath, SourceFolderPath);
        DataManager.SetConfigValue(() => DataManager.DestinationFolderPath, DestinationFolderPath);
        DataManager.SetConfigValue(() => DataManager.GitHubLink, GitHubLink);
        DataManager.SetConfigValue(() => DataManager.MinecraftVersion, MinecraftVersion);
        DataManager.SetConfigValue(() => DataManager.LoaderType, LoaderType);
        DataManager.SetConfigValue(() => DataManager.LoaderVersion, LoaderVersion);
        DataManager.SetConfigValue(() => DataManager.RecommendedRam, RecommendedRam);
        DataManager.SetConfigValue(() => DataManager.ModpackName, ModpackName);
        DataManager.SetConfigValue(() => DataManager.ModpackVersion, ModpackVersion);
        DataManager.SetConfigValue(() => DataManager.CurseforgeId, CurseforgeId);
        DataManager.SetConfigValue(() => DataManager.ModpackAuthor, ModpackAuthor);
        DataManager.SaveConfig();
    }

    public static void HandleTerminalInput(string? input)
    {
        if (Validator.IsNullOrWhiteSpace(input)) return;
        Task.Run(() => Terminal.RunCommand(input));
    }


    private static async Task CancelAllOperations()
    {
        Log.Warning("User requested cancellation");
        await Cts.CancelAsync();
        await _logCts.CancelAsync();
        Cts = new CancellationTokenSource();
        _logCts = new CancellationTokenSource();
    }

    private async Task FetchLoaderVersionsAsync(string? loaderType, string? minecraftVersion)

    {
        if (Validator.IsNullOrWhiteSpace(loaderType) || Validator.IsNullOrWhiteSpace(minecraftVersion))
            return;

        try
        {
            Log.Information($"Fetching available versions for {loaderType}");
            string? oldSelected = LoaderVersion;
            LoaderVersion = null;

            List<string> versions = await MavenService.FetchAvailableVersions(loaderType, minecraftVersion);
            LoaderVersionOptions.Clear();

            switch (loaderType)
            {
                case "NeoForge":
                {
                    for (int i = versions.Count - 1; i >= 0; i--)
                        LoaderVersionOptions.Add(versions[i]);
                    break;
                }
                case "Forge":
                {
                    foreach (string version in versions)
                        LoaderVersionOptions.Add(version);
                    break;
                }
                default:
                    throw new ArgumentException($"Unknown loader: {loaderType}");
            }

            if (oldSelected != null && LoaderVersionOptions.Contains(oldSelected))
                LoaderVersion = oldSelected;

            Log.Debug($"Available for {loaderType} {MinecraftVersion} are: [{string.Join(", ", LoaderVersionOptions)}]");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch loader versions: {ex.Message}");
        }
    }

    private static async Task OpenGitHubRepoLink(string? url)
    {
        if (Validator.IsNullOrWhiteSpace(url))
            return;

        if (!GitService.TryValidateGitHubUrl(url, out _))
        {
            Log.Warning("Invalid GitHub repository URL");
            return;
        }

        try
        {
            ProcessStartInfo processInfo = new() { FileName = url, UseShellExecute = true };
            Process process = new() { StartInfo = processInfo };
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open GitHub repository page: {ex.Message}");
        }
    }

    private static async Task GenerateChangelog(string? sourceFolder, string? destinationFolder, string? packName, string? packVersion, CancellationToken ct)
    {
        if (Validator.IsNullOrWhiteSpace(sourceFolder) || Validator.IsNullOrWhiteSpace(destinationFolder) || Validator.IsNullOrWhiteSpace(packName) ||
            Validator.IsNullOrWhiteSpace(packVersion))
            return;

        string root = await FileUtil.PrepareRootFolderAsync(destinationFolder, packName, packVersion);

        string export = Path.Join(sourceFolder, "local", "kubejs", "export");
        await ChangelogGenerator.GenerateFullChangelogAsync(root, export, packVersion, ct: ct);
    }

    private static async Task PushToGitHub(string? url, Task? silentCloneTask, CancellationToken ct = default)
    {
        if (Validator.DirectoryEmpty(GitService.GitRepoPath) || Validator.IsNullOrWhiteSpace(url))
            return;

        if (silentCloneTask is { IsCompleted: false })
        {
            Log.Debug("Waiting for silent clone task to finish");
            await silentCloneTask;
        }

        List<string> files =
        [
            Path.Combine(GitService.GitRepoPath, "config", "bcc-common.toml"),
            Path.Combine(GitService.GitRepoPath, "config", "crash_assistant", "modlist.json")
        ];

        StringBuilder autoCommitMessage = new();
        autoCommitMessage.AppendLine("automatic-chore: Updated bcc-common.toml and modlist.json");

        string token = await TokenManager.RetrieveTokenValueByTypeAsync(TokenType.GitHub);

        int staged = 0;
        foreach (string file in files)
        {
            staged += await GitService.StageAsync(file, ct);
        }

        bool success = false;
        if(staged > 0) success = await GitService.CommitAsync(token, autoCommitMessage.ToString(), ct);
        
        if (success) await GitService.PushAsync(token, url, ct);
    }

    private static async Task GenerateRepo(string repoUrl, Task? silentCloneTask, CancellationToken ct = default)
    {
        if (silentCloneTask is { IsCompleted: false })
        {
            Log.Debug("Waiting for silent clone task to finish");
            await silentCloneTask;
        }

        await GitService.CloneOrUpdateRepoAsync(repoUrl, ct);
    }

    private string FinalPath(string? destinationPath)
    {
        return Validator.IsNullOrWhiteSpace(destinationPath) ? string.Empty : Path.Combine(destinationPath, $"{ModpackName ?? "unknown"}", $"{ModpackVersion ?? "0.0.0"}");
    }

    private static async Task CopyFilesBasedOnRepoStatus(string sourceFolder, string targetDir, List<Rule> baseRules, List<Rule> localRules, List<Rule> repoRules, string? overwritePath,
        CancellationToken ct)
    {
        if (Validator.DirectoryExists(GitService.GitRepoPath))
        {
            Task copyRepoTask = FileCopyHelper.CopyFilesAsync(GitService.GitRepoPath, targetDir, repoRules, ct);
            Task copyFolderTask = FileCopyHelper.CopyFilesAsync(sourceFolder, targetDir, localRules, ct);
            Task overwriteTask = Task.Delay(0, ct);
            if (Validator.DirectoryExists(overwritePath, LogEventLevel.Information))
                overwriteTask = FileCopyHelper.CopyFilesAsync(GitService.GitRepoPath, overwritePath, repoRules, ct);
            await Task.WhenAll(copyRepoTask, copyFolderTask, overwriteTask);
        }
        else
        {
            Log.Warning("Invalid Repo target, falling back to local files. These files may not be up to date!");
            await FileCopyHelper.CopyFilesAsync(sourceFolder, targetDir, baseRules, ct);
        }
    }

    public static async Task GenerateConfigs(string sourceDir, string targetDir, string overwriteDir, string? curseforgeId, string packName, string packVersion,
        CancellationToken ct = default)
    {
        curseforgeId ??= string.Empty;

        List<string> paths =
        [
            GitService.GitRepoPath,
            targetDir,
            overwriteDir
        ];

        List<ModInfo> modInfos = await JarUtil.GetJarInfoInDirectoryAsync(Path.Combine(sourceDir, "mods"), ct);

        foreach (string path in paths.Where(Directory.Exists))
        {
            await ConfigBuilder.GenerateModListFileAsync(Path.Combine(path, "config", "bcc-common.toml"), modInfos, ct);
            await ConfigBuilder.GenerateBccConfig(Path.Combine(path, "config", "bcc-common.toml"), curseforgeId, packName, packVersion, ct);
        }
    }

    public async Task GenerateClientAsync(string? sourceFolder, string? destinationFolder, string? gitHubRepoLink, Task? silentCloneTask, string? mcVersion, string? loaderType,
        string? loaderVersion, string? packName, string? packVersion, string? packAuthor, string? curseforgeId, CancellationToken ct)
    {
        if (Validator.IsNullOrWhiteSpace(sourceFolder) || Validator.IsNullOrWhiteSpace(destinationFolder) || Validator.IsNullOrWhiteSpace(gitHubRepoLink) ||
            Validator.IsNullOrWhiteSpace(mcVersion) || Validator.IsNullOrWhiteSpace(loaderType) || Validator.IsNullOrWhiteSpace(loaderVersion) ||
            Validator.IsNullOrWhiteSpace(packName) || Validator.IsNullOrWhiteSpace(packVersion) || Validator.IsNullOrWhiteSpace(packAuthor))
            return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        Log.Information($"Generating client files for {sourceFolder} to {destinationFolder}");

        List<Rule> baseRules = CreateRules();
        List<Rule> localRules = CreateRules(true);
        List<Rule> repoRules = CreateRules(true, true);

        string root = await FileUtil.PrepareRootFolderAsync(destinationFolder, packName, packVersion);
        string targetDir = Path.Combine(root, $"{packName}-ClientExport");
        Directory.CreateDirectory(targetDir);

        await GenerateRepo(gitHubRepoLink, silentCloneTask, ct);
        await CopyFilesBasedOnRepoStatus(sourceFolder, targetDir, baseRules, localRules, repoRules, DataManager.ClientOverwritePath, ct);
        await FileUtil.ApplyFilters(Path.Combine(targetDir, "mods"), true, ct);

        bool tryParse = TryParse(RecommendedRam, out int recommendedRam);
        await JsonBuilder.GenerateManifest(targetDir, mcVersion, packVersion, packAuthor, packName, loaderType, loaderVersion, tryParse ? recommendedRam : 0, ct);

        await GenerateConfigs(sourceFolder, targetDir, DataManager.ClientOverwritePath, curseforgeId, packName, packVersion, ct);

        await FileUtil.CleanUpEmptyFoldersAsync(targetDir);
        Log.Information("Zipping client files");
        await FileUtil.ZipFolderAsync(targetDir, Path.Combine(FinalPath(destinationFolder), $"{packName}-{packVersion}.zip"), ct);
        Log.Information("Successfully generated client files");

        stopwatch.Stop();
        Log.Information($"Client files generation took {stopwatch.ElapsedMilliseconds}ms");
    }

    public async Task GenerateServerAsync(string? sourceFolder, string? destinationFolder, string? gitHubRepoLink, Task? silentCloneTask, string? loaderType,
        string? loaderVersion, string? packName, string? packVersion, string? curseforgeId, CancellationToken ct)
    {
        if (Validator.IsNullOrWhiteSpace(sourceFolder) || Validator.IsNullOrWhiteSpace(destinationFolder) || Validator.IsNullOrWhiteSpace(gitHubRepoLink) ||
            Validator.IsNullOrWhiteSpace(loaderType) || Validator.IsNullOrWhiteSpace(loaderVersion) || Validator.IsNullOrWhiteSpace(packName) ||
            Validator.IsNullOrWhiteSpace(packVersion))
            return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        Log.Information($"Generating server files for {sourceFolder} to {destinationFolder}");

        List<Rule> baseRules = CreateRules(false, false, false);
        List<Rule> localRules = CreateRules(true, false, false);
        List<Rule> repoRules = CreateRules(true, true, false);

        string root = await FileUtil.PrepareRootFolderAsync(destinationFolder, packName, packVersion);
        string targetDir = Path.Combine(root, $"{packName}-ServerExport");
        Directory.CreateDirectory(targetDir);

        await GenerateRepo(gitHubRepoLink, silentCloneTask, ct);
        await CopyFilesBasedOnRepoStatus(sourceFolder, targetDir, baseRules, localRules, repoRules, DataManager.ServerOverwritePath, ct);
        await FileUtil.ApplyFilters(Path.Combine(targetDir, "mods"), false, ct);

        Log.Debug("Attempting modloader installer download from maven");
        await MavenService.DownloadLoader(loaderType, loaderVersion, targetDir);

        if (Validator.DirectoryExists(TemplateFolderPath, LogEventLevel.Debug) && Directory.GetFiles(TemplateFolderPath).Length > 0)
        {
            List<Rule> templateRules = [new("README", "md", false)];
            await FileCopyHelper.CopyFilesAsync(TemplateFolderPath, targetDir, templateRules, ct);
            Log.Information("Successfully copied template files");
        }
        else
        {
            Log.Information("Template folder is empty or does not exist");
        }

        await GenerateConfigs(sourceFolder, targetDir, DataManager.ServerOverwritePath, curseforgeId, packName, packVersion, ct);

        Log.Information("Zipping server files");
        await FileUtil.ZipFolderAsync(targetDir, Path.Combine(FinalPath(destinationFolder), $"ServerFiles-{packVersion}.zip"), ct);
        Log.Information("Successfully generated server files");

        stopwatch.Stop();
        Log.Information($"Server files generation took {stopwatch.ElapsedMilliseconds}ms");
    }

    private static List<Rule> CreateRules(bool hasGitHub = false, bool isRepo = false, bool isClient = true)
    {
        List<Rule> ruleSet;
        
        List<Rule> rules = [
            new("config", "directory", true),
            new("defaultconfigs", "directory", true),
            new("kubejs", "directory", true),
            new("packmenu", "directory", true)
        ];

        List<Rule> localServerRules = [new("mods", "directory", true)];

        List<Rule> localClientRules = [
            new("shaderpacks", "directory", true),
            new("resourcepacks", "directory", true),
            new("minecraftinstance", "json", true)
        ];

        if (isClient)
            localServerRules.AddRange(localClientRules);

        if (hasGitHub)
        {
            ruleSet = isRepo ? rules : localServerRules;
            foreach (Rule rule in ruleSet) Log.Debug($"Using {rule}");
            return ruleSet;
        }

        rules.AddRange(localServerRules);
        ruleSet = isRepo ? rules : localServerRules;
        foreach (Rule rule in ruleSet) Log.Debug($"Using {rule}");
        return ruleSet;
    }
}