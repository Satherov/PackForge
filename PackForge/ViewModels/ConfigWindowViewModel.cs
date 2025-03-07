using PackForge.Core.Data;

namespace PackForge.ViewModels;

public class ConfigWindowViewModel
{
    // Config Flags
    public bool BccConfig { get; set; }
    public bool ModListConfig { get; set; }

    public ConfigWindowViewModel()
    {
        LoadData();
    }

    private void LoadData()
    {
        BccConfig = DataManager.GetConfigValue(() => DataManager.BccConfig);
        ModListConfig = DataManager.GetConfigValue(() => DataManager.ModListConfig);
    }

    public void SaveData()
    {
        DataManager.SetAndSaveConfigValue(() => DataManager.BccConfig, BccConfig);
        DataManager.SetAndSaveConfigValue(() => DataManager.ModListConfig, ModListConfig);
    }
}