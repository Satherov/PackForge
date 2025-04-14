using CommunityToolkit.Mvvm.ComponentModel;
using PackForge.Core.Data;

namespace PackForge.ViewModels;

public partial class ConfigWindowViewModel : ObservableObject
{
    [ObservableProperty] private static bool _flagMcreator;
    [ObservableProperty] private static int _flagDataOnly;
    [ObservableProperty] private static bool _bccConfig;
    [ObservableProperty] private static bool _modListConfig;

    public ConfigWindowViewModel()
    {
        LoadData();
    }

    private void LoadData()
    {
        DataManager.LoadConfig();
        FlagMcreator = DataManager.GetConfigValue(() => DataManager.FlagMcreator);
        FlagDataOnly = DataManager.GetConfigValue(() => DataManager.FlagDataOnly);
        BccConfig = DataManager.GetConfigValue(() => DataManager.BccConfig);
        ModListConfig = DataManager.GetConfigValue(() => DataManager.ModListConfig);
    }

    public void SaveData()
    {
        DataManager.SetConfigValue(() => DataManager.FlagMcreator, FlagMcreator);
        DataManager.SetConfigValue(() => DataManager.FlagDataOnly, FlagDataOnly);
        DataManager.SetConfigValue(() => DataManager.BccConfig, BccConfig);
        DataManager.SetConfigValue(() => DataManager.ModListConfig, ModListConfig);
        DataManager.SaveConfig();
    }
}