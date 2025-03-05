using System;
using System.Diagnostics;
using System.Threading.Tasks;
using PackForge.Core;
using PackForge.Core.Data;
using ReactiveUI;
using Serilog;

namespace PackForge.ViewModels;

public class TokenWindowViewModel : ReactiveObject
{
    private string? _gitHubToken;
    private string? _curseforgeToken;

    public string? GitHubToken
    {
        get => _gitHubToken;
        set => this.RaiseAndSetIfChanged(ref _gitHubToken, value);
    }

    public string? CurseforgeToken
    {
        get => _curseforgeToken;
        set => this.RaiseAndSetIfChanged(ref _curseforgeToken, value);
    }

    public AsyncRelayCommand? OpenGitHubTokenPageCommand { get; }
    public AsyncRelayCommand? EncryptGitHubTokenCommand { get; }

    public AsyncRelayCommand? OpenCurseforgeTokenPageCommand { get; }
    public AsyncRelayCommand? EncryptCurseforgeTokenCommand { get; }

    public TokenWindowViewModel()
    {
        _ = InitializeTokensAsync();

        OpenGitHubTokenPageCommand = new AsyncRelayCommand(async () => await Task.Run(() => OpenPage("https://github.com/settings/tokens/new")));
        EncryptGitHubTokenCommand = new AsyncRelayCommand(async () => await Task.Run(() => TokenManager.StoreTokenAsync("GitHub", GitHubToken)));

        OpenCurseforgeTokenPageCommand = new AsyncRelayCommand(async () => await Task.Run(() => OpenPage("https://console.curseforge.com/#/api-keys")));
        EncryptCurseforgeTokenCommand = new AsyncRelayCommand(async () => await Task.Run(() => TokenManager.StoreTokenAsync("Curseforge", CurseforgeToken)));
    }

    private async Task InitializeTokensAsync()
    {
        GitHubToken = await TokenManager.RetrieveTokenAsync("GitHub") ?? string.Empty;
        CurseforgeToken = await TokenManager.RetrieveTokenAsync("Curseforge") ?? string.Empty;
    }


    private static Task OpenPage(string url)
    {
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
            Log.Error($"Failed to open page: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}