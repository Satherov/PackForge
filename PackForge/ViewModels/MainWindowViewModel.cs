using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PackForge.Core;
using PackForge.Core.Builders;
using PackForge.Core.Data;
using PackForge.Core.Helpers;
using PackForge.Core.Service;
using PackForge.Core.Util;
using PackForge.Logging;
using ReactiveUI;
using Serilog;

namespace PackForge.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    private CancellationTokenSource? _cts;

    // File Paths
    private string? _sourceFolderPath;
    private string? _destinationFolderPath;

    public string? SourceFolderPath
    {
        get => _sourceFolderPath;
        set => this.RaiseAndSetIfChanged(ref _sourceFolderPath, value);
    }

    public string? DestinationFolderPath
    {
        get => _destinationFolderPath;
        set => this.RaiseAndSetIfChanged(ref _destinationFolderPath, value);
    }

    // GitHub
    private string? _gitHubRepoLink;

    public string? GitHubRepoLink
    {
        get => _gitHubRepoLink;
        set => this.RaiseAndSetIfChanged(ref _gitHubRepoLink, value);
    }

    // Loader
    private string? _minecraftVersion;
    private string? _loaderType;
    private string? _loaderVersion;

    public ObservableCollection<string> LoaderTypeOptions { get; } =
    [
        "NeoForge",
        "Forge"
    ];

    public ObservableCollection<string> MinecraftVersionOptions { get; } =
    [
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
        "1.7.10"
    ];

    public ObservableCollection<string> LoaderVersionOptions { get; set; } = [];

    public string? MinecraftVersion
    {
        get => _minecraftVersion;
        set => this.RaiseAndSetIfChanged(ref _minecraftVersion, value);
    }

    public string? LoaderType
    {
        get => _loaderType;
        set => this.RaiseAndSetIfChanged(ref _loaderType, value);
    }

    public string? LoaderVersion
    {
        get => _loaderVersion;
        set => this.RaiseAndSetIfChanged(ref _loaderVersion, value);
    }

    // Modpack Info
    private string? _modpackName;
    private string? _modpackVersion;
    private string? _curseforgeId;
    private string? _modpackAuthor;

    public string? ModpackName
    {
        get => _modpackName;
        set => this.RaiseAndSetIfChanged(ref _modpackName, value);
    }

    public string? ModpackVersion
    {
        get => _modpackVersion;
        set => this.RaiseAndSetIfChanged(ref _modpackVersion, value);
    }

    public string? CurseforgeId
    {
        get => _curseforgeId;
        set => this.RaiseAndSetIfChanged(ref _curseforgeId, value);
    }

    public string? ModPackAuthor
    {
        get => _modpackAuthor;
        set => this.RaiseAndSetIfChanged(ref _modpackAuthor, value);
    }

    // Excluded Mods
    private List<string>? _excludedMods;

    private List<string>? ExcludedMods
    {
        get => _excludedMods;
        set => this.RaiseAndSetIfChanged(ref _excludedMods, value);
    }

    public string ExcludedModsDisplay
    {
        get => string.Join(Environment.NewLine, ExcludedMods ??= []);
        set
        {
            ExcludedMods = value.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries).ToList();
            this.RaisePropertyChanged();
        }
    }

    // Log Entries
    private readonly ObservableCollection<LogEntry> _logEntries = App.GlobalLog.LogEntries;
    private readonly ObservableCollection<LogEntry> _filteredLogEntries = [];
    public ReadOnlyObservableCollection<LogEntry> FilteredLogEntries { get; }

    private bool _showDebugLogs;

    public bool ShowDebugLogs
    {
        get => _showDebugLogs;
        set
        {
            if (_showDebugLogs == value) return;
            _showDebugLogs = value;
            OnPropertyChanged();
            RebuildFilteredLogEntries();
        }
    }
    
    public new event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Rebuilds the filtered log entries based on- or excluding debug logs
    private void RebuildFilteredLogEntries()
    {
        _filteredLogEntries.Clear();

        foreach (var entry in _logEntries)
        {
            if (!ShowDebugLogs && entry.Level.Equals("DEBUG", StringComparison.InvariantCultureIgnoreCase)) continue;
            _filteredLogEntries.Add(entry);
        }

        OnPropertyChanged(nameof(FilteredLogEntries));
    }

    // Commands
    public AsyncRelayCommand? KillTasksCommand { get; }
    public AsyncRelayCommand? OpenSourceFolderCommand { get; }
    public AsyncRelayCommand? OpenDestinationFolderCommand { get; }
    public AsyncRelayCommand? OpenGitHubRepoCommand { get; }
    public AsyncRelayCommand? FetchLoaderVersionCommand { get; }

    public AsyncRelayCommand? GenerateClientCommand { get; }
    public AsyncRelayCommand? GenerateServerCommand { get; }
    public AsyncRelayCommand? GenerateChangelogCommand { get; }
    public AsyncRelayCommand? PushToGitHubCommand { get; }
    public AsyncRelayCommand? PushToCurseforgeCommand { get; }
    public AsyncRelayCommand? OpenConfigWindowCommand { get; }
    public AsyncRelayCommand? OpenTokenWindowCommand { get; }

    public MainWindowViewModel()
    {
        LoadData();
        FilteredLogEntries = new ReadOnlyObservableCollection<LogEntry>(_filteredLogEntries);
        _logEntries.CollectionChanged += (_, _) => RebuildFilteredLogEntries();
        RebuildFilteredLogEntries();

        KillTasksCommand = new AsyncRelayCommand(async () => await Task.Run(CancelAllOperations));

        OpenSourceFolderCommand = new AsyncRelayCommand(async () => SourceFolderPath = await Task.Run(() => FileHelper.OpenFolderAsync(SourceFolderPath)));
        OpenDestinationFolderCommand = new AsyncRelayCommand(async () => DestinationFolderPath = await Task.Run(() => FileHelper.OpenFolderAsync(DestinationFolderPath)));
        OpenGitHubRepoCommand = new AsyncRelayCommand(async () => await Task.Run(() => OpenGitHubRepoLink(GitHubRepoLink)));
        FetchLoaderVersionCommand = new AsyncRelayCommand(async () => await Task.Run(FetchLoaderVersionsAsync));

        GenerateClientCommand = new AsyncRelayCommand(async () => await Task.Run(() => GenerateClientAsync(SourceFolderPath, DestinationFolderPath, GitHubRepoLink)));

        //PushToCurseforgeCommand = new AsyncRelayCommand(async () => await Task.Run());

        OpenConfigWindowCommand = new AsyncRelayCommand(async () => await Task.Run(WindowManager.ShowConfigWindow));
        OpenTokenWindowCommand = new AsyncRelayCommand(async () => await Task.Run(WindowManager.ShowTokenWindow));
    }

    private void LoadData()
    {
        ConfigData.LoadConfig();

        SourceFolderPath = ConfigData.GetConfigValue(() => ConfigData.SourceFolderPath);
        DestinationFolderPath = ConfigData.GetConfigValue(() => ConfigData.DestinationFolderPath);
        GitHubRepoLink = ConfigData.GetConfigValue(() => ConfigData.GitHubRepoLink);

        MinecraftVersion = ConfigData.GetConfigValue(() => ConfigData.MinecraftVersion);
        LoaderType = ConfigData.GetConfigValue(() => ConfigData.LoaderType);
        LoaderVersion = ConfigData.GetConfigValue(() => ConfigData.LoaderVersion);

        ModpackName = ConfigData.GetConfigValue(() => ConfigData.ModpackName);
        ModpackVersion = ConfigData.GetConfigValue(() => ConfigData.ModpackVersion);
        CurseforgeId = ConfigData.GetConfigValue(() => ConfigData.CurseforgeId);
        ModPackAuthor = ConfigData.GetConfigValue(() => ConfigData.ModPackAuthor);

        ExcludedMods = ConfigData.GetConfigValue(() => ConfigData.ExcludedMods);
    }

    public void SaveData()
    {
        ConfigData.SetConfigValue(() => ConfigData.SourceFolderPath, SourceFolderPath);
        ConfigData.SetConfigValue(() => ConfigData.DestinationFolderPath, DestinationFolderPath);
        ConfigData.SetConfigValue(() => ConfigData.GitHubRepoLink, GitHubRepoLink);

        ConfigData.SetConfigValue(() => ConfigData.MinecraftVersion, MinecraftVersion);
        ConfigData.SetConfigValue(() => ConfigData.LoaderType, LoaderType);
        ConfigData.SetConfigValue(() => ConfigData.LoaderVersion, LoaderVersion);

        ConfigData.SetConfigValue(() => ConfigData.ModpackName, ModpackName);
        ConfigData.SetConfigValue(() => ConfigData.ModpackVersion, ModpackVersion);
        ConfigData.SetConfigValue(() => ConfigData.CurseforgeId, CurseforgeId);
        ConfigData.SetConfigValue(() => ConfigData.ModPackAuthor, ModPackAuthor);

        ConfigData.SetConfigValue(() => ConfigData.ExcludedMods, ExcludedMods);

        ConfigData.SaveConfig();
    }

    private void CancelAllOperations()
    {
        _cts?.Cancel();
        Log.Warning("User requested cancellation");
    }
    
    /// <summary>
    /// Stores a list of all available versions for the selected mod loader in the LoaderVersionOptions  
    /// </summary>
    /// <exception cref="ArgumentException">If you somehow managed to select a loader version that is not on the list</exception>
    private async Task FetchLoaderVersionsAsync()
    {
        _cts = new CancellationTokenSource();

        if(Validator.CheckNullOrWhiteSpace(LoaderType) ||
           Validator.CheckNullOrWhiteSpace(MinecraftVersion)) return;

        try
        {
            var versions = await MavenService.FetchAvailableVersions(LoaderType!, MinecraftVersion!);

            LoaderVersionOptions.Clear();
            switch (LoaderType)
            {
                case "NeoForge":
                    // Reverse the list to show the latest version first because neo returns them in the wrong order :shrug:
                    for (var i = versions.Count - 1; i >= 0; i--) LoaderVersionOptions.Add(versions[i]);
                    break;
                case "Forge":
                    // Add all versions to the list
                    foreach (var version in versions) LoaderVersionOptions.Add(version);
                    break;
                
                // How did we get here
                default: throw new ArgumentException($"Unknown loader: {LoaderType}");
            }

            // Reset the selected version
            LoaderVersion = null;

            Log.Information($"Fetched available versions for {LoaderType}");
            Log.Debug($"Available maven versions for {LoaderType} {MinecraftVersion} are: [{string.Join(", ", LoaderVersionOptions)}]");
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Operation was killed by the user");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch loader versions: {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Opens the GitHub repository link in the default browser if the URL is valid
    /// </summary>
    /// <param name="url">URL to open</param>
    private static async Task OpenGitHubRepoLink(string? url)
    {
        if(Validator.CheckNullOrWhiteSpace(url)) return;

        if (!await GitService.IsValidGitHubRepoAsync(url!))
        {
            Log.Warning($"Invalid GitHub repository URL");
            return;
        }

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };

            Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open GitHub repository page: {ex.Message}");
        }
    }

    private async Task GenerateClientAsync(string? sourceFolder, string? destinationFolder, string? gitHubRepoLink)
    {
        _cts = new CancellationTokenSource();

        if(Validator.CheckNullOrWhiteSpace(sourceFolder) ||
           Validator.CheckNullOrWhiteSpace(destinationFolder) ||
           Validator.CheckNullOrWhiteSpace(gitHubRepoLink)) return;

        var baseRules = CreateRules(hasGitHub: false , isRepo: false);
        var localRules = CreateRules(hasGitHub: true , isRepo: false);
        var repoRules = CreateRules(hasGitHub: true , isRepo: true);

        try
        {
            Log.Information($"Generating client files for {sourceFolder} to {destinationFolder}");

            var targetDir = Path.Join(await FileHelper.PrepareRootFolderAsync(destinationFolder, ModpackName), $"{ModpackName}-{ModpackVersion ??= "0.0.0"}-ClientExport");
            var repoValid = await GitService.IsValidGitHubRepoAsync(gitHubRepoLink!);
            if (!repoValid) Log.Warning($"Invalid GitHub repository link");
            
            var tempRepoPath = repoValid ? await Task.Run(() => GitService.CloneGitHubRepo(gitHubRepoLink!, _cts.Token), _cts.Token) : string.Empty;
            
            if (Directory.Exists(tempRepoPath))
            {
                await FileHelper.CopyMatchingFilesAsync(tempRepoPath, targetDir, repoRules, _cts.Token);
                await FileHelper.CopyMatchingFilesAsync(sourceFolder!, targetDir, localRules, _cts.Token);
            }
            else
            {
                Log.Warning($"Invalid Repo target, falling back to local files");
                await FileHelper.CopyMatchingFilesAsync(sourceFolder!, targetDir, baseRules, _cts.Token);
            }

            await GitService.DeleteTempRepo(tempRepoPath);
            
            await JsonBuilder.GenerateManifest(
                path: targetDir, 
                mcVersion: MinecraftVersion!, 
                packVersion: ModpackVersion!, 
                packAuthor: ModPackAuthor!, 
                packName: ModpackName!, 
                loaderType: LoaderType!, 
                loaderVersion: LoaderVersion!);

            await FileHelper.CleanUpClientFolder(targetDir);
            
            Log.Information("Zipping client files");
            var zipPath = Path.Combine(Path.Join(destinationFolder, ModpackName), $"{ModpackName}-{ModpackVersion ??= "0.0.0"}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            await Task.Run(() => ZipFile.CreateFromDirectory(targetDir, zipPath), _cts.Token);
            Log.Information($"Successfully generated client files");
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Operation was killed by the user");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    private static Dictionary<string, (List<string>, bool)> CreateRules(bool hasGitHub = false, bool isRepo = false)
    {
        var rules = new Dictionary<string, (List<string>, bool)>
        {
            { "config", ([], false) },
            { "defaultconfigs", ([], false) },
            { "kubejs", ([], false) },
            { "packmenu", ([], false) }
        };
        var localRules = new Dictionary<string, (List<string>, bool)> 
        {
            { "mods", ([], false) },
            { "shaderpacks", ([], false)},
            { "resourcepacks", ([], false) },
            { "minecraftinstance.json", ([], false) },
        };
        if (hasGitHub) return isRepo ? rules : localRules;
        
        foreach (var kvp in localRules)
        {
            rules[kvp.Key] = kvp.Value;
        }
        return isRepo ? rules : localRules;
    }
}