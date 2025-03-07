using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using PackForge.Logging;
using Serilog;

namespace PackForge.Core.Data;

public static class DataManager
{
    private static ConfigDataContainer _container = new();

    /// <summary>
    /// Path to the config file.
    /// </summary>
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PackForge", "data", "config.json"
    );

    /// <summary>
    /// Default options for JSON serialization.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Checks if the config file exists.
    /// </summary>
    private static bool ConfigExists()
    {
        return File.Exists(AppDataPath);
    }
    
    /// <summary>
    /// Creates the config file with default values.
    /// </summary>
    private static void CreateConfig()
    {
        _container = new ConfigDataContainer();
        SaveConfig();
    }

    /// <summary>
    /// Loads configuration from the JSON file.
    /// </summary>
    public static void LoadConfig()
    {
        if (!ConfigExists())
        {
            Log.Warning("Configuration file not found. Creating a new one...");
            CreateConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(AppDataPath);
            var config = JsonSerializer.Deserialize<ConfigDataContainer>(json, JsonOptions);

            _container = config ?? throw new NullReferenceException("Config could not be deserialized.");
            Log.Information("Config loaded successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Error loading config: {ex.Message}");
        }
    }

    private static void SaveConfigValue<T>(Expression<Func<T>> propertyExpression)
    {
        try
        {
            if (propertyExpression.Body is MemberExpression { Member: PropertyInfo property })
            {
                var configDict = new Dictionary<string, object>();

                if (ConfigExists())
                {
                    var json = File.ReadAllText(AppDataPath);
                    configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOptions) ??
                                 new Dictionary<string, object>();
                }

                var value = property.GetValue(_container);
                configDict[property.Name] = value ?? string.Empty;

                Directory.CreateDirectory(Path.GetDirectoryName(AppDataPath)!);
                var updatedJson = JsonSerializer.Serialize(configDict, JsonOptions);
                File.WriteAllText(AppDataPath, updatedJson);

                Log.Debug($"Successfully saved '{property.Name}'");
            }
            else
            {
                Log.Warning($"Invalid property expression: {propertyExpression}. Unable to save value");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error saving config value for '{propertyExpression}': {e.Message}");
        }
    }
    
    public static void SetAndSaveConfigValue<T>(Expression<Func<T>> propertyExpression, T value)
    {
        try
        {
            if (propertyExpression.Body is MemberExpression { Member: PropertyInfo property })
            {
                property.SetValue(_container, value);
                SaveConfigValue(propertyExpression);
                return;
            }

            Log.Warning($"Invalid property expression: {propertyExpression}. Unable to set value");
        }
        catch (Exception e)
        {
            Log.Error($"Error setting config value for '{propertyExpression}': {e.Message}");
        }
    }


    /// <summary>
    /// Saves the current configuration to the JSON file.
    /// </summary>
    public static void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppDataPath)!);
            var json = JsonSerializer.Serialize(_container, JsonOptions);
            File.WriteAllText(AppDataPath, json);

            Log.Information("Config saved successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Error saving config: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns a specific config value dynamically.
    /// </summary>
    /// <returns>Config Value requested</returns>
    public static T? GetConfigValue<T>(Expression<Func<T>> propertyExpression)
    {
        try
        {
            if (propertyExpression.Body is MemberExpression { Member: PropertyInfo property })
                return (T?)property.GetValue(_container);

            Log.Warning($"Invalid property expression: {propertyExpression}. Unable to get value");
            return default;
        }
        catch (Exception e)
        {
            Log.Error($"Error retrieving config value for '{propertyExpression}': {e.Message}");
            return default;
        }
    }

    /// <summary>
    /// Sets a specific config value dynamically.
    /// </summary>
    public static void SetConfigValue<T>(Expression<Func<T>> propertyExpression, T value)
    {
        try
        {
            if (propertyExpression.Body is MemberExpression { Member: PropertyInfo property })
            {
                property.SetValue(_container, value);
                var formattedValue = value is IEnumerable<string> list
                    ? $"[{string.Join(", ", list)}]"
                    : value?.ToString();
                Log.Debug($"Successfully set '{property.Name}' to {formattedValue}");
                return;
            }

            Log.Warning($"Invalid property expression: {propertyExpression}. Unable to set value");
        }
        catch (Exception e)
        {
            Log.Error($"Error setting config value for '{propertyExpression}': {e.Message}");
        }
    }


    // Direct Access Properties
    public static string SourceFolderPath
    {
        get => _container.SourceFolderPath;
        set => _container.SourceFolderPath = value;
    }

    public static string DestinationFolderPath
    {
        get => _container.DestinationFolderPath;
        set => _container.DestinationFolderPath = value;
    }

    public static string GitHubRepoLink
    {
        get => _container.GitHubLink;
        set => _container.GitHubLink = value;
    }

    public static string LoaderType
    {
        get => _container.LoaderType;
        set => _container.LoaderType = value;
    }

    public static string LoaderVersion
    {
        get => _container.LoaderVersion;
        set => _container.LoaderVersion = value;
    }

    public static string MinecraftVersion
    {
        get => _container.MinecraftVersion;
        set => _container.MinecraftVersion = value;
    }

    public static string ModpackName
    {
        get => _container.ModpackName;
        set => _container.ModpackName = value;
    }

    public static string ModpackVersion
    {
        get => _container.ModpackVersion;
        set => _container.ModpackVersion = value;
    }

    public static string CurseforgeId
    {
        get => _container.CurseforgeId;
        set => _container.CurseforgeId = value;
    }

    public static string ModPackAuthor
    {
        get => _container.Author;
        set => _container.Author = value;
    }

    
    public static List<string> ExcludedMods
    {
        get => _container.ExcludedMods;
        set => _container.ExcludedMods = value;
    }

    
    public static string ClientOverwritePath
    {
        get => _container.ClientOverwritePath;
        set => _container.ClientOverwritePath = value;
    }
    
    public static string ServerOverwritePath
    {
        get => _container.ServerOverwritePath;
        set => _container.ServerOverwritePath = value;
    }

    public static bool BccConfig
    {
        get => _container.BccConfig;
        set => _container.BccConfig = value;
    }

    public static bool ModListConfig
    {
        get => _container.ModListConfig;
        set => _container.ModListConfig = value;
    }
}

/// <summary>
/// Holds the serializable configuration data.
/// </summary>
public class ConfigDataContainer
{
    public string SourceFolderPath { get; set; } = string.Empty;
    public string DestinationFolderPath { get; set; } = string.Empty;
    public string GitHubLink { get; set; } = string.Empty;
    public string LoaderType { get; set; } = string.Empty;
    public string LoaderVersion { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public string ModpackName { get; set; } = string.Empty;
    public string ModpackVersion { get; set; } = string.Empty;
    public string CurseforgeId { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    
    public List<string> ExcludedMods { get; set; } = [];
    
    public string ClientOverwritePath { get; set; } = string.Empty;
    public string ServerOverwritePath { get; set; } = string.Empty;
    
    public bool BccConfig { get; set; }
    public bool ModListConfig { get; set; }
}