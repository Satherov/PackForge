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
using PackForge.Logging;
using ReactiveUI;
using Serilog;

namespace PackForge.ViewModels;

public class MainWindowViewModel : ReactiveObject
{
    
    private readonly Task _silentCloneTask;
    private readonly CancellationTokenSource _cts = new();

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

    public ReadOnlyObservableCollection<string> LoaderTypeOptions { get; init;  } = new(
        [
            "NeoForge", 
            "Forge"
        ]
    );

    public ReadOnlyObservableCollection<string> MinecraftVersionOptions { get; } = new(
    
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
            "1.7.10", "1.7.2",
            "1.6.4", "1.6.3", "1.6.2", "1.6.1",
            "1.5.2", "1.5.1", "1.5",
            "1.4.7", "1.4.6", "1.4.5", "1.4.4", "1.4.2", "1.4.0",
            "1.3.2",
            "1.2.5", "1.2.4", "1.2.3",
            "1.1",
        ]
    );

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
            OnPropertyChanged(nameof(FilteredLogEntries));
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
        try
        {
            _filteredLogEntries.Clear();

            foreach (var entry in _logEntries)
            {
                if (!ShowDebugLogs && entry.Level.Equals("DEBUG", StringComparison.InvariantCultureIgnoreCase))
                    continue;
                _filteredLogEntries.Add(entry);
            }

            OnPropertyChanged(nameof(FilteredLogEntries));
        }
        catch (Exception ex)
        {
            Log.Fatal($"Fatal Error while rebuilding filtered log entries. This should never happen! Report to the author: {ex.Message}");
        }
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
    public AsyncRelayCommand? OpenOverwriteWindowCommand { get; }
    public AsyncRelayCommand? OpenTemplateFolderCommand { get; }


    public MainWindowViewModel()
    {
        LoadData();
        
        FilteredLogEntries = new ReadOnlyObservableCollection<LogEntry>(_filteredLogEntries);
        _logEntries.CollectionChanged += (_, _) => RebuildFilteredLogEntries();
        RebuildFilteredLogEntries();
        
        if(!Validator.IsNullOrWhiteSpace(GitHubRepoLink, logLevel: "debug")) _silentCloneTask = GitService.SilentCloneGitHubRepo(GitHubRepoLink, _cts.Token);
        else Log.Debug("No GitHub repository link found in config, skipping silent clone");

        KillTasksCommand = new AsyncRelayCommand(async () => await Task.Run(CancelAllOperations));

        OpenSourceFolderCommand = new AsyncRelayCommand(async () => SourceFolderPath = await Task.Run(() => FileHelper.OpenFolderAsync(SourceFolderPath)));
        OpenDestinationFolderCommand = new AsyncRelayCommand(async () => DestinationFolderPath = await Task.Run(() => FileHelper.OpenFolderAsync(DestinationFolderPath)));
        OpenGitHubRepoCommand = new AsyncRelayCommand(async () => await Task.Run(() => OpenGitHubRepoLink(GitHubRepoLink)));
        FetchLoaderVersionCommand = new AsyncRelayCommand(async () => await Task.Run(FetchLoaderVersionsAsync));

        GenerateClientCommand = new AsyncRelayCommand(async () => await Task.Run(() => GenerateClientAsync(SourceFolderPath, DestinationFolderPath, GitHubRepoLink, _silentCloneTask)));
        GenerateServerCommand = new AsyncRelayCommand(async () => await Task.Run(() => GenerateServerAsync(SourceFolderPath, DestinationFolderPath, GitHubRepoLink, _silentCloneTask)));

        OpenConfigWindowCommand = new AsyncRelayCommand(async () => await Task.Run(() => WindowHelper.ShowWindow(() => WindowHelper.ConfigWindow)));
        OpenTokenWindowCommand = new AsyncRelayCommand(async () => await Task.Run(() => WindowHelper.ShowWindow(() => WindowHelper.TokenWindow)));
        OpenOverwriteWindowCommand = new AsyncRelayCommand(async () => await Task.Run(() => WindowHelper.ShowWindow(() => WindowHelper.OverwriteWindow)));
        OpenTemplateFolderCommand = new AsyncRelayCommand(async () => await Task.Run(() => Process.Start(new ProcessStartInfo { FileName = TemplateBuilder.TemplateFolderPath, UseShellExecute = true })));
    }

    private void LoadData()
    {
        DataManager.LoadConfig();
        
        SourceFolderPath = DataManager.GetConfigValue(() => DataManager.SourceFolderPath);
        DestinationFolderPath = DataManager.GetConfigValue(() => DataManager.DestinationFolderPath);
        GitHubRepoLink = DataManager.GetConfigValue(() => DataManager.GitHubRepoLink);

        MinecraftVersion = DataManager.GetConfigValue(() => DataManager.MinecraftVersion);
        LoaderType = DataManager.GetConfigValue(() => DataManager.LoaderType);
        LoaderVersion = DataManager.GetConfigValue(() => DataManager.LoaderVersion);
        LoaderVersionOptions = Validator.IsNullOrWhiteSpace(LoaderVersion, logLevel: "none") ? new ObservableCollection<string>([LoaderVersion!]) : [];

        ModpackName = DataManager.GetConfigValue(() => DataManager.ModpackName);
        ModpackVersion = DataManager.GetConfigValue(() => DataManager.ModpackVersion);
        CurseforgeId = DataManager.GetConfigValue(() => DataManager.CurseforgeId);
        ModPackAuthor = DataManager.GetConfigValue(() => DataManager.ModPackAuthor);

        ExcludedMods = DataManager.GetConfigValue(() => DataManager.ExcludedMods);
    }

    public void SaveData()
    {
        DataManager.SetConfigValue(() => DataManager.SourceFolderPath, SourceFolderPath);
        DataManager.SetConfigValue(() => DataManager.DestinationFolderPath, DestinationFolderPath);
        DataManager.SetConfigValue(() => DataManager.GitHubRepoLink, GitHubRepoLink);

        DataManager.SetConfigValue(() => DataManager.MinecraftVersion, MinecraftVersion);
        DataManager.SetConfigValue(() => DataManager.LoaderType, LoaderType);
        DataManager.SetConfigValue(() => DataManager.LoaderVersion, LoaderVersion);

        DataManager.SetConfigValue(() => DataManager.ModpackName, ModpackName);
        DataManager.SetConfigValue(() => DataManager.ModpackVersion, ModpackVersion);
        DataManager.SetConfigValue(() => DataManager.CurseforgeId, CurseforgeId);
        DataManager.SetConfigValue(() => DataManager.ModPackAuthor, ModPackAuthor);

        DataManager.SetConfigValue(() => DataManager.ExcludedMods, ExcludedMods);

        DataManager.SaveConfig();
    }

    private void CancelAllOperations()
    {
        Log.Warning("User requested cancellation");
        _cts.Cancel();
    }
    
    /// <summary>
    /// Stores a list of all available versions for the selected mod loader in the LoaderVersionOptions  
    /// </summary>
    /// <exception cref="ArgumentException">If you somehow managed to select a loader version that is not on the list</exception>
    private async Task FetchLoaderVersionsAsync()
    {
        if(Validator.IsNullOrWhiteSpace(LoaderType) ||
           Validator.IsNullOrWhiteSpace(MinecraftVersion)) return;

        try
        {
            Log.Information($"Fetching available versions for {LoaderType}");

            var oldSelected = LoaderVersion;
            LoaderVersion = null;
            
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
            
            if (oldSelected != null && LoaderVersionOptions.Contains(oldSelected)) LoaderVersion = oldSelected;

            Log.Debug($"Available for {LoaderType} {MinecraftVersion} are: [{string.Join(", ", LoaderVersionOptions)}]");
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Fetch Loader operation was killed by the user");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to fetch loader versions: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the GitHub repository link in the default browser if the URL is valid
    /// </summary>
    /// <param name="url">URL to open</param>
    private static async Task OpenGitHubRepoLink(string? url)
    {
        if(Validator.IsNullOrWhiteSpace(url)) return;

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

    private async Task GenerateClientAsync(string? sourceFolder, string? destinationFolder, string? gitHubRepoLink, Task? silentCloneTask = null)
    {
        if(Validator.IsNullOrWhiteSpace(sourceFolder) ||
           Validator.IsNullOrWhiteSpace(destinationFolder) ||
           Validator.IsNullOrWhiteSpace(gitHubRepoLink)) return;

        var baseRules = CreateRules(hasGitHub: false , isRepo: false, isClient: true);
        var localRules = CreateRules(hasGitHub: true , isRepo: false, isClient: true);
        var repoRules = CreateRules(hasGitHub: true , isRepo: true, isClient: true);

        try
        {
            Log.Information($"Generating client files for {sourceFolder} to {destinationFolder}");

            var root = await FileHelper.PrepareRootFolderAsync(destinationFolder, ModpackName, ModpackVersion);
            var targetDir = FileHelper.PrepareFolder(root,  $"{ModpackName}-ClientExport");
            await GenerateRepo(GitHubRepoLink, silentCloneTask, _cts.Token);
            
            await GenerateRepo(GitHubRepoLink, silentCloneTask, _cts.Token);
            
            if (Validator.DirectoryExists(GitService.GetTempRepoPath()))
            {
                await FileHelper.CopyMatchingFilesAsync(GitService.GetTempRepoPath(), targetDir, repoRules, _cts.Token);
                await FileHelper.CopyMatchingFilesAsync(sourceFolder!, targetDir, localRules, _cts.Token);
                
                if(!Validator.IsNullOrWhiteSpace(DataManager.ClientOverwritePath, logLevel: "debug", variableName:"Client Overwrite")) await FileHelper.CopyMatchingFilesAsync(sourceFolder!, DataManager.ClientOverwritePath, repoRules, _cts.Token);
            }
            else
            {
                Log.Warning($"Invalid Repo target, falling back to local files");
                Log.Warning($"These files may not be up to date!");
                await FileHelper.CopyMatchingFilesAsync(sourceFolder!, targetDir, baseRules, _cts.Token);
            }
            
            await JsonBuilder.GenerateManifest(
                path: targetDir, 
                mcVersion: MinecraftVersion!, 
                packVersion: ModpackVersion!, 
                packAuthor: ModPackAuthor!, 
                packName: ModpackName!, 
                loaderType: LoaderType!, 
                loaderVersion: LoaderVersion!);

            await FileHelper.CleanUpEmptyFolders(targetDir);
            
            Log.Information("Zipping client files");
            
            var zipPath = Path.Combine(Path.Join(destinationFolder, ModpackName), $"{ModpackVersion ??= "0.0.0"}", $"{ModpackName ??= "Unknown"}-{ModpackVersion ??= "0.0.0"}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            await Task.Run(() => ZipFile.CreateFromDirectory(targetDir, zipPath), _cts.Token);
            
            Log.Information($"Successfully generated client files");
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Generate client files operation was killed by the user");
        }
    }
    
    private async Task GenerateServerAsync(string? sourceFolder, string? destinationFolder, string? gitHubRepoLink, Task? silentCloneTask = null)
    {
        if(Validator.IsNullOrWhiteSpace(sourceFolder) ||
           Validator.IsNullOrWhiteSpace(destinationFolder) ||
           Validator.IsNullOrWhiteSpace(gitHubRepoLink)) return;

        var baseRules = CreateRules(hasGitHub: false , isRepo: false, isClient: false);
        var localRules = CreateRules(hasGitHub: true , isRepo: false, isClient: false);
        var repoRules = CreateRules(hasGitHub: true , isRepo: true, isClient: false);

        try
        {
            Log.Information($"Generating server files for {sourceFolder} to {destinationFolder}");

            var root = await FileHelper.PrepareRootFolderAsync(destinationFolder, ModpackName, ModpackVersion);
            var targetDir = FileHelper.PrepareFolder(root,  $"{ModpackName}-ServerExport");
            await GenerateRepo(GitHubRepoLink, silentCloneTask, _cts.Token);
            
            if (Validator.DirectoryExists(GitService.GetTempRepoPath()))
            {
                await FileHelper.CopyMatchingFilesAsync(GitService.GetTempRepoPath(), targetDir, repoRules, _cts.Token);
                await FileHelper.CopyMatchingFilesAsync(sourceFolder!, targetDir, localRules, _cts.Token);
                
                if(!Validator.IsNullOrWhiteSpace(DataManager.ServerOverwritePath, logLevel: "debug", variableName:"Server Overwrite")) await FileHelper.CopyMatchingFilesAsync(sourceFolder!, DataManager.ServerOverwritePath, repoRules, _cts.Token);
            }
            else
            {
                Log.Warning($"Invalid Repo target, falling back to local files");
                Log.Warning($"These files may not be up to date!");
                await FileHelper.CopyMatchingFilesAsync(sourceFolder!, targetDir, baseRules, _cts.Token);
            }
            
            Log.Debug("Attempting modloader installer download from maven");
            await MavenService.DownloadLoader(LoaderType, LoaderVersion, targetDir);
            
            // Log.Information("Zipping server files");
            //
            // var zipPath = Path.Combine(Path.Join(destinationFolder, ModpackName), $"{ModpackVersion ??= "0.0.0"}", $"{ModpackName ??= "Unknown"}-{ModpackVersion ??= "0.0.0"}.zip");
            // if (File.Exists(zipPath)) File.Delete(zipPath);
            // await Task.Run(() => ZipFile.CreateFromDirectory(targetDir, zipPath), _cts.Token);
            
            Log.Information($"Successfully generated server files");
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Generate server files operation was killed by the user");
        }
    }

    private static async Task GenerateRepo(string? gitHubRepoLink, Task? silentCloneTask, CancellationToken cts)
    {
        if(silentCloneTask is { IsCompleted: false })
        {
            Log.Debug("Waiting for silent clone task to finish");
            await silentCloneTask;
        }
        
        if (!Validator.DirectoryExists(GitService.GetTempRepoPath()) && !Validator.IsNullOrWhiteSpace(GitService.GetTempRepoPath()))
        {
            var repoValid = await GitService.IsValidGitHubRepoAsync(gitHubRepoLink!);
            if (!repoValid) Log.Warning($"Invalid GitHub repository link");
            else await Task.Run(() => GitService.CloneGitHubRepo(gitHubRepoLink!, cts), cts);
        }
    }

    public enum FileSystemType
    {
        Directory,
        File
    }
    
    private static Dictionary<string, (FileSystemType, (List<string>, bool))> CreateRules(bool hasGitHub = false, bool isRepo = false, bool isClient = true, List<string>? excludedMods = null)
    {
        excludedMods ??= [];
        
        var rules = new Dictionary<string, (FileSystemType, (List<string>, bool))>
        {
            { "config", (FileSystemType.Directory, ([], false)) },
            { "defaultconfigs", (FileSystemType.Directory, ([], false)) },
            { "kubejs", (FileSystemType.Directory, ([], false)) },
            { "packmenu", (FileSystemType.Directory, ([], false)) }
        };
        var localServerRules = new Dictionary<string, (FileSystemType, (List<string>, bool))> 
        {
            { "mods", (FileSystemType.Directory,(excludedMods, false)) },
        };
        var localClientRules = new Dictionary<string, (FileSystemType, (List<string>, bool))> 
        {
            { "shaderpacks", (FileSystemType.Directory, ([], false))},
            { "resourcepacks", (FileSystemType.Directory, ([], false)) },
            { "minecraftinstance.json", (FileSystemType.File, ([], false)) },
        };
        
        if (isClient)
        {
            foreach (var kvp in localClientRules)
            {
                localServerRules[kvp.Key] = kvp.Value;
            }
        }
        
        if (hasGitHub) return isRepo ? rules : localServerRules;
        
        foreach (var kvp in localServerRules)
        {
            rules[kvp.Key] = kvp.Value;
        }
        return isRepo ? rules : localServerRules;
    }
}