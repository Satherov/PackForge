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
        BccConfig = ConfigData.GetConfigValue(() => ConfigData.BccConfig);
        ModListConfig = ConfigData.GetConfigValue(() => ConfigData.ModListConfig);
    }

    public void SaveData()
    {
        ConfigData.SetConfigValue(() => ConfigData.BccConfig, BccConfig);
        ConfigData.SetConfigValue(() => ConfigData.ModListConfig, ModListConfig);

        ConfigData.SaveConfigValue(() => ConfigData.BccConfig);
        ConfigData.SaveConfigValue(() => ConfigData.ModListConfig);
    }
}