using System.Threading.Tasks;
using PackForge.Core;
using PackForge.Core.Data;
using PackForge.Core.Util;
using ReactiveUI;

namespace PackForge.ViewModels;

public class OverwriteWindowViewModel : ReactiveObject
{
    private string? _clientPath;
    private string? _serverPath;

    public string? ClientPath
    {
        get => _clientPath;
        set => this.RaiseAndSetIfChanged(ref _clientPath, value);
    }

    public string? ServerPath
    {
        get => _serverPath;
        set => this.RaiseAndSetIfChanged(ref _serverPath, value);
    }

    public AsyncRelayCommand? OpenClientFolderCommand { get; }
    public AsyncRelayCommand? OpenServerFolderCommand { get; }

    public OverwriteWindowViewModel()
    {
        LoadData();
        OpenClientFolderCommand = new AsyncRelayCommand(async () => ClientPath = await FileHelper.OpenFolderAsync(ClientPath, WindowHelper.OverwriteWindow));
        OpenServerFolderCommand = new AsyncRelayCommand(async () => ServerPath = await FileHelper.OpenFolderAsync(ServerPath, WindowHelper.OverwriteWindow));
    }

    private void LoadData()
    {
        DataManager.LoadConfig();
        ClientPath = DataManager.GetConfigValue(() => DataManager.ClientOverwritePath);
        ServerPath = DataManager.GetConfigValue(() => DataManager.ServerOverwritePath);
    }

    public void SaveData()
    {
        DataManager.SetConfigValue(() => DataManager.ClientOverwritePath, ClientPath);
        DataManager.SetConfigValue(() => DataManager.ServerOverwritePath, ServerPath);
        DataManager.SaveConfig();
    }
}