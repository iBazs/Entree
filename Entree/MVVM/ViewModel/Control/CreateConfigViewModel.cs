// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Serilog;
using Wpf.Ui.Controls;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.Services;
using RelayCommand = Entree.Core.BaseClass.RelayCommand;

// ReSharper disable AsyncVoidLambda

namespace Entree.MVVM.ViewModel.Control;

public class CreateConfigViewModel : Core.BaseClass.ViewModel
{
   private readonly IAppSettings       _appSettings;
   private readonly INavigationService _navigationService;
   private readonly ISearchService     _searchService;

   public CreateConfigViewModel(ISearchService searchService, INavigationService navigationService,
                                IAppSettings appSettings)
   {
      _searchService                 =  searchService;
      _navigationService             =  navigationService;
      _appSettings                   =  appSettings;
      SearchConfigs                  =  [];
      _searchService.PropertyChanged += SearchServiceOnPropertyChanged;
      AddNewConfigCommand            =  new RelayCommand(_ => { AddNewConfig(); });
      RemoveConfigCommand            =  new RelayCommand(RemoveConfig);
      CopyConfigCommand              =  new RelayCommand(CopyConfig);
      SaveToConfigFileCommand        =  new RelayCommand(_ => { SaveToConfigFile(); });
      LoadToConfigFileCommand        =  new RelayCommand(_ => { LoadToConfigFile(); });
      DragDropLoadConfigFileCommand  =  new RelayCommand(LoadToConfigFile);
      ClearAllConfigCommand          =  new RelayCommand(_ => { SearchConfigs.Clear(); });
      StartSearchCommand             =  new RelayCommand(async _ => { await SearchHandler(); },
                                                         _ => IsRunning || SearchConfigs.Count > 0);
   }

   public ObservableCollection<SearchConfigViewModel> SearchConfigs { get; }

   public RelayCommand AddNewConfigCommand           { get; }
   public RelayCommand RemoveConfigCommand           { get; }
   public RelayCommand CopyConfigCommand             { get; }
   public RelayCommand SaveToConfigFileCommand       { get; }
   public RelayCommand LoadToConfigFileCommand       { get; }
   public RelayCommand ClearAllConfigCommand         { get; }
   public RelayCommand StartSearchCommand            { get; }
   public RelayCommand DragDropLoadConfigFileCommand { get; }

   private bool _isStarting;

   private async Task SearchHandler()
   {
      if (IsRunning)
      {
         await Task.Run(Stop);
         return;
      }

      if (_isStarting || SearchConfigs.Count == 0) return;
      _isStarting = true;
      try
      {
         await Start(SearchConfigs.ToList());
      }
      finally
      {
         _isStarting = false;
      }
   }

   private async Task Start(IReadOnlyList<SearchConfigViewModel> configs)
   {
      try
      {
         _navigationService.NavigateTo<SearchViewModel>();
         await _searchService.Search(configs);
      }
      catch (ArgumentNullException ex)
      {
         Log.Error("Invalid search configuration: {Message}", ex.Message);
      }
      catch (DirectoryNotFoundException ex)
      {
         Log.Error("Directory not found: {Message}", ex.Message);
      }
      catch (UnauthorizedAccessException ex)
      {
         Log.Error("Access denied during search: {Message}", ex.Message);
      }
      catch (OperationCanceledException)
      {
         Log.Information("Search was cancelled by the user.");
      }
      catch (OutOfMemoryException ex)
      {
         Log.Error("Out of memory during search: {Message}", ex.Message);
      }
      catch (Exception ex)
      {
         Log.Error("Unexpected error during search: {Type} - {Message}", ex.GetType().Name, ex.Message);
      }

      Log.Information("Search completed.");
   }

   private async Task Stop()
   {
      try
      {
         await Task.Run(() => _searchService.Stop());
         Log.Warning("All searches were cancelled.");
      }
      catch (Exception ex)
      {
         Log.Error("Error({Type}) was happened: {Message}", ex.GetType(), ex.Message);
      }
   }

   private void LoadToConfigFile(object? obj = null)
   {
      OpenFileDialog dialog = new() { Filter = "Entrée files (*.entree)|*.entree" };
      if (obj == null)
      {
         if (dialog.ShowDialog() != true) return;
         CheckConfig(dialog.FileName);
      }
      else
      {
         var path = obj.ToString();
         if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
         CheckConfig(path);
      }

      return;

      void CheckConfig(string filePath)
      {
         try
         {
            var result = ReportHelper.LoadSearchConfigurations(filePath);
            foreach (var config in result)
               Application.Current.Dispatcher.Invoke(() => SearchConfigs.Add(config));
            Log.Information("Config file({FilePath}) loaded successfully.", filePath);
         }
         catch (Exception e)
         {
            Log.Error("Cannot read config file. Please check if its in correct format.\n{@error}", e.Message);
         }
      }
   }

   private void SaveToConfigFile()
   {
      if (SearchConfigs.Count == 0) return;
      SaveFileDialog dialog = new() { FileName = "config.entree", Filter = "Entrée files (*.entree)|*.entree" };
      if (dialog.ShowDialog() != true) return;
      List<SearchConfigModel> configs = [];
      configs.AddRange(SearchConfigs.Select(searchConfig => searchConfig.SearchConfig));
      try
      {
         File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(configs));
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Cannot save config file {FilePath}", dialog.FileName);
      }
   }

   private void AddNewConfig()
   {
      SearchConfigs.Add(new SearchConfigViewModel(Environment.CurrentDirectory, _appSettings.AppResultPath));
   }

   private void RemoveConfig(object parameter)
   {
      var result = SearchConfigs.FirstOrDefault(input => input.Id == (Guid)parameter);
      if (result != null) SearchConfigs.Remove(result);
   }

   private void CopyConfig(object parameter)
   {
      var result = SearchConfigs.FirstOrDefault(input => input.Id == (Guid)parameter);
      if (result == null) return;
      var newConfig = result.Clone();
      try
      {
         var index = SearchConfigs.IndexOf(result);
         SearchConfigs.Insert(index, newConfig);
      }
      catch (ArgumentOutOfRangeException)
      {
         SearchConfigs.Add(newConfig);
      }
      catch (Exception ex)
      {
         Log.Error("Failed to copy config. {@error}", ex.Message);
      }
   }

   private bool              _isRunning;
   private string            _buttonText    = "Start";
   private SymbolIcon        _icon          = new(SymbolRegular.Play24, 21);
   private ControlAppearance _appAppearance = ControlAppearance.Primary;

   public bool IsRunning
   {
      get => _isRunning;
      private set
      {
         _isRunning = value;
         Application.Current.Dispatcher.Invoke(() =>
         {
            if (_isRunning)
            {
               AppAppearance = ControlAppearance.Danger;
               ButtonText    = "Stop";
               Icon          = new SymbolIcon(SymbolRegular.Stop24, 21);
            }
            else
            {
               AppAppearance = ControlAppearance.Primary;
               ButtonText    = "Start";
               Icon          = new SymbolIcon(SymbolRegular.Play24, 21);
            }

            CommandManager.InvalidateRequerySuggested();
         });
         OnPropertyChanged();
      }
   }

   private void SearchServiceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
   {
      if (e.PropertyName != nameof(_searchService.IsSearchRunning)) return;
      IsRunning = _searchService.IsSearchRunning;
   }

   public string ButtonText
   {
      get => _buttonText;
      private set => Set(ref _buttonText, value);
   }

   public SymbolIcon Icon
   {
      get => _icon;
      private set => Set(ref _icon, value);
   }

   public ControlAppearance AppAppearance
   {
      get => _appAppearance;
      private set => Set(ref _appAppearance, value);
   }
}