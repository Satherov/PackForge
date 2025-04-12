using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using PackForge.Core.Util;
using Serilog;
using Serilog.Events;

namespace PackForge.Core.Data;

public static class DataManager
{
    private static ConfigDataContainer _container = new();
    private static readonly string AppDataPath = Path.Combine(App.AppDataPath, "data", "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static void LoadConfig()
    {
        if (!Validator.FileExists(AppDataPath, LogEventLevel.Debug))
        {
            Log.Information("No config file found. Generating default config.");
            SaveConfig();
        }

        try
        {
            string json = File.ReadAllText(AppDataPath);
            _container = JsonSerializer.Deserialize<ConfigDataContainer>(json, JsonOptions)
                         ?? throw new NullReferenceException("Config could not be deserialized.");
            Log.Debug("Config loaded successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Error loading config: {ex.Message}");
        }
    }

    public static void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppDataPath)!);
            string json = JsonSerializer.Serialize(_container, JsonOptions);
            File.WriteAllText(AppDataPath, json);
            Log.Debug("Config saved successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Error saving config: {ex.Message}");
        }
    }

    public static T? GetConfigValue<T>(Expression<Func<T>> propertyExpression)
    {
        try
        {
            if (propertyExpression.Body is MemberExpression { Member: PropertyInfo property })
                return (T?)property.GetValue(_container);

            Log.Warning($"Invalid property expression: {propertyExpression}. Unable to get value");
        }
        catch (Exception e)
        {
            Log.Error($"Error retrieving config value for '{propertyExpression}': {e.Message}");
        }

        return default;
    }

    public static void SetConfigValue<T>(Expression<Func<T>> propertyExpression, T value)
    {
        try
        {
            if (propertyExpression.Body is not MemberExpression { Member: PropertyInfo property })
            {
                Log.Warning($"Invalid property expression: {propertyExpression}. Unable to set value");
                return;
            }

            property.SetValue(_container, value);

            string? formattedValue = value is IEnumerable<string> list
                ? $"[{string.Join(", ", list)}]"
                : value?.ToString();

            Log.Debug($"Set '{property.Name}' to {formattedValue}");
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

    public static string GitHubLink
    {
        get => _container.GitHubLink;
        set => _container.GitHubLink = value;
    }

    public static string MinecraftVersion
    {
        get => _container.MinecraftVersion;
        set => _container.MinecraftVersion = value;
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

    public static string RecommendedRam
    {
        get => _container.RecommendedRam;
        set => _container.RecommendedRam = value;
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

    public static string ModpackAuthor
    {
        get => _container.ModpackAuthor;
        set => _container.ModpackAuthor = value;
    }

    public static string CurseforgeId
    {
        get => _container.CurseforgeId;
        set => _container.CurseforgeId = value;
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

    public static bool FlagMcreator
    {
        get => _container.FlagMcreator;
        set => _container.FlagMcreator = value;
    }

    public static bool FlagDataOnly
    {
        get => _container.FlagDataOnly;
        set => _container.FlagDataOnly = value;
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

    public static List<string> ExcludedCommon
    {
        get => _container.ExcludedCommon;
        set => _container.ExcludedCommon = value;
    }

    public static List<string> ExcludedClient
    {
        get => _container.ExcludedClient;
        set => _container.ExcludedClient = value;
    }

    public static List<string> ExcludedServer
    {
        get => _container.ExcludedServer;
        set => _container.ExcludedServer = value;
    }

    public static List<string> ExcludedAuthors
    {
        get => _container.ExcludedAuthors;
        set => _container.ExcludedAuthors = value;
    }
}

public class ConfigDataContainer
{
    public string SourceFolderPath { get; set; } = string.Empty;
    public string DestinationFolderPath { get; set; } = string.Empty;
    public string GitHubLink { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public string LoaderType { get; set; } = string.Empty;
    public string LoaderVersion { get; set; } = string.Empty;
    public string RecommendedRam { get; set; } = string.Empty;
    public string ModpackName { get; set; } = string.Empty;
    public string ModpackVersion { get; set; } = string.Empty;
    public string ModpackAuthor { get; set; } = string.Empty;
    public string CurseforgeId { get; set; } = string.Empty;
    public string ClientOverwritePath { get; set; } = string.Empty;
    public string ServerOverwritePath { get; set; } = string.Empty;
    public bool FlagMcreator { get; set; }
    public bool FlagDataOnly { get; set; }
    public bool BccConfig { get; set; }
    public bool ModListConfig { get; set; }
    public List<string> ExcludedCommon { get; set; } = [];
    public List<string> ExcludedClient { get; set; } = [];
    public List<string> ExcludedServer { get; set; } = [];
    public List<string> ExcludedAuthors { get; set; } = [];
}