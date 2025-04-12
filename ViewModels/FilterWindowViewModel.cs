using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PackForge.Core.Data;

namespace PackForge.ViewModels;

public partial class FilterWindowViewModel : ObservableObject
{
    private List<string>? _excludedCommon;

    public string PrettyExcludedCommon
    {
        get
        {
            _excludedCommon ??= [];
            return string.Join(Environment.NewLine, _excludedCommon);
        }
        
        set
        {
            _excludedCommon = value.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries).ToList();
            OnPropertyChanged();
        }
    }
    private List<string>? _excludedClient;
    public string PrettyExcludedClient
    {
        get
        {
            _excludedClient ??= [];
            return string.Join(Environment.NewLine, _excludedClient);
        }
        
        set
        {
            _excludedClient = value.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries).ToList();
            OnPropertyChanged();
        }
    }
    private List<string>? _excludedServer;
    public string PrettyExcludedServer
    {
        get
        {
            _excludedServer ??= [];
            return string.Join(Environment.NewLine, _excludedServer);
        }
        
        set
        {
            _excludedServer = value.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries).ToList();
            OnPropertyChanged();
        }
    }
    private List<string>? _excludedAuthors;
    public string PrettyExcludedAuthors
    {
        get
        {
            _excludedAuthors ??= [];
            return string.Join(Environment.NewLine, _excludedAuthors);
        }
        
        set
        {
            _excludedAuthors = value.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries).ToList();
            OnPropertyChanged();
        }
    }

    public FilterWindowViewModel()
    {
        LoadAll();
    }

    private void LoadAll()
    {
        DataManager.LoadConfig();
        _excludedCommon = DataManager.GetConfigValue(() => DataManager.ExcludedCommon);
        _excludedClient = DataManager.GetConfigValue(() => DataManager.ExcludedClient);
        _excludedServer = DataManager.GetConfigValue(() => DataManager.ExcludedServer);
        _excludedAuthors = DataManager.GetConfigValue(() => DataManager.ExcludedAuthors);
    }

    public void SaveData()
    {
        DataManager.SetConfigValue(() => DataManager.ExcludedCommon, _excludedCommon);
        DataManager.SetConfigValue(() => DataManager.ExcludedClient, _excludedClient);
        DataManager.SetConfigValue(() => DataManager.ExcludedServer, _excludedServer);
        DataManager.SetConfigValue(() => DataManager.ExcludedAuthors, _excludedAuthors);
        DataManager.SaveConfig();
    }
}