// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;

namespace Entree.MVVM.ViewModel;

public class SearchConfigViewModel : Core.BaseClass.ViewModel
{
   private readonly Stopwatch        _backgroundTimer = new();
   private          string           _currentState    = "Queued";
   private          TimeSpan         _elapsedTime;
   private          string           _fileName = "";
   private          bool             _isRunning, _isCancelled, _isError, _isDone;
   private          bool             _isScheduled = true;
   private          string?          _selectedDepthOption;
   private          DateTime?        _startTime, _endTime;
   private          DispatcherTimer? _timer;

   public SearchConfigViewModel(SearchConfigModel searchConfig)
   {
      SearchConfig = searchConfig;
      Id           = Guid.NewGuid();

      InitializeDirectories(
                            string.IsNullOrEmpty(SearchConfig.SearchDirectory)
                                     ? Directory.GetCurrentDirectory()
                                     : Path.TrimEndingDirectorySeparator(SearchConfig.SearchDirectory),
                            string.IsNullOrEmpty(SearchConfig.SaveDirectory)
                                     ? Directory.GetCurrentDirectory()
                                     : SearchConfig.SaveDirectory
                           );

      InitializeDepthSettings(SearchConfig.DepthLevel);

      InitializeCommands();
   }

   public SearchConfigViewModel(string? searchPath = "",
                                string? savePath = "",
                                SearchFor searchFor = SearchFor.Files,
                                int depth = -1,
                                bool inheritedAccess = true,
                                bool showOwner = true,
                                bool showGroups = true,
                                bool showMatrix = true,
                                bool openResult = true,
                                bool isLocalSearch = false,
                                ExportAs exportAs = ExportAs.Xlsx,
                                ObservableCollection<string>? excludedAccounts = null,
                                ObservableCollection<PrincipalContext>? domains = null)
   {
      SearchConfig = new SearchConfigModel
                     {
                              SearchDirectory = string.IsNullOrEmpty(searchPath)
                                       ? Directory.GetCurrentDirectory()
                                       : Path.TrimEndingDirectorySeparator(searchPath),
                              SaveDirectory = string.IsNullOrEmpty(savePath)
                                       ? Environment.CurrentDirectory
                                       : savePath
                     };

      Id = Guid.NewGuid();

      InitializeDirectories(SearchConfig.SearchDirectory, SearchConfig.SaveDirectory);
      InitializeDepthSettings(depth);

      SearchFor        = searchFor;
      Domains          = domains ?? [];
      ExcludedAccounts = excludedAccounts ?? [];

      IsLocalSearch  = isLocalSearch;
      Inherited      = inheritedAccess;
      ShowOwner      = showOwner;
      ShowGroups     = showGroups;
      ShowMatrix     = showMatrix;
      OpenResult     = openResult;
      StringExportAs = exportAs == ExportAs.Xlsx ? "Excel (.xlsx)" : "CSV comma (.csv)";

      InitializeCommands();
   }

   public bool CanBeCancelled => !IsFinished && IsScheduled;

   public bool IsFinished => IsDone || IsCancelled || IsError;

   public bool IsRunning
   {
      get => _isRunning;
      set
      {
         if (_isRunning == value) return;
         _isRunning = value;
         if (value)
         {
            CurrentState = "Running";
            TimerStart();
         }
         else
         {
            TimerStop();
         }

         OnPropertyChanged();
         OnPropertyChanged(nameof(CanBeCancelled));
      }
   }

   public bool IsDone
   {
      get => _isDone;
      set
      {
         if (_isDone == value) return;
         if (value) CurrentState = "Done";
         _isDone = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(CanBeCancelled));
      }
   }

   public bool IsScheduled
   {
      get => _isScheduled;
      set
      {
         if (value) CurrentState = "Queued";
         _isScheduled = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(CanBeCancelled));
      }
   }

   public bool IsCancelled
   {
      get => _isCancelled;
      set
      {
         if (value && CanBeCancelled) CurrentState = "Cancelled";
         _isCancelled = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(CanBeCancelled));
      }
   }

   public bool IsError
   {
      get => _isError;
      set
      {
         if (value) CurrentState = "Error";
         _isError = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(CanBeCancelled));
      }
   }

   public string CurrentState
   {
      get => _currentState;
      set
      {
         _currentState = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(StatusColor));
      }
   }

   public string ElapsedTime => _startTime is null ? "-" : _elapsedTime.ToString(@"hh\:mm\:ss");

   public string DetailedTimeInfo
   {
      get
      {
         if (_startTime == null)
            return "Search not started";
         var startStr = _startTime.Value.ToString("HH:mm:ss");
         if (IsRunning)
         {
            var elapsed = _backgroundTimer.Elapsed;
            return $@"Started: {startStr} | Running for: {elapsed:hh\:mm\:ss}";
         }

         if (_endTime == null) return $"Started: {startStr} | Status unknown";
         var endStr    = _endTime.Value.ToString("HH:mm:ss");
         var totalTime = _endTime.Value - _startTime.Value;
         return $@"Started: {startStr} | Ended: {endStr} | Duration: {totalTime:hh\:mm\:ss}";
      }
   }

   public SearchConfigModel SearchConfig { get; }
   public Guid              Id           { get; }

   public string SearchDirectory
   {
      get => SearchConfig.SearchDirectory;
      set
      {
         SearchConfig.SearchDirectory = value;
         OnPropertyChanged();
      }
   }

   public string SaveDirectory
   {
      get => SearchConfig.SaveDirectory;
      set
      {
         SearchConfig.SaveDirectory = value;
         OnPropertyChanged();
      }
   }

   public string FileName
   {
      get => _fileName;
      set => Set(ref _fileName, value);
   }

   public SearchFor SearchFor
   {
      get => SearchConfig.SearchFor;
      set
      {
         SearchConfig.SearchFor = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(SearchForText));
      }
   }

   public int DepthLevel
   {
      get => SearchConfig.DepthLevel;
      set
      {
         SearchConfig.DepthLevel = value;
         OnPropertyChanged();
      }
   }

   public bool IsLocalSearch
   {
      get => SearchConfig.LocalSearch;
      set
      {
         SearchConfig.LocalSearch = value;
         OnPropertyChanged();
      }
   }

   public bool Inherited
   {
      get => SearchConfig.InheritedAccess;
      set
      {
         SearchConfig.InheritedAccess = value;
         OnPropertyChanged();
      }
   }

   public bool ShowOwner
   {
      get => SearchConfig.ShowOwner;
      set
      {
         SearchConfig.ShowOwner = value;
         OnPropertyChanged();
      }
   }

   public bool ShowGroups
   {
      get => SearchConfig.ShowGroups;
      set
      {
         SearchConfig.ShowGroups = value;
         OnPropertyChanged();
      }
   }

   public bool ShowMatrix
   {
      get => SearchConfig.ShowMatrix;
      set
      {
         SearchConfig.ShowMatrix = value;
         OnPropertyChanged();
      }
   }

   public bool OpenResult
   {
      get => SearchConfig.OpenResults;
      set
      {
         SearchConfig.OpenResults = value;
         OnPropertyChanged();
      }
   }

   public string StringExportAs
   {
      get => SearchConfig.ExportAs == ExportAs.Xlsx ? "Excel (.xlsx)" : "CSV comma (.csv)";
      set
      {
         SearchConfig.ExportAs = value.Contains("Excel") ? ExportAs.Xlsx : ExportAs.Csv;
         OnPropertyChanged();
      }
   }

   public ExportAs ExportAs => SearchConfig.ExportAs;

   [JsonIgnore]
   public ObservableCollection<string> DomainNames { get; } = [];

   public ObservableCollection<PrincipalContext> Domains
   {
      get => SearchConfig.Domains;
      private init
      {
         SearchConfig.Domains = value;
         OnPropertyChanged();
      }
   }

   public ObservableCollection<string> ExcludedAccounts
   {
      get => SearchConfig.ExcludedAccounts;
      private init
      {
         SearchConfig.ExcludedAccounts = value;
         OnPropertyChanged();
      }
   }

   public Dictionary<SearchFor, string> SearchForChoices { get; } = new()
                                                                    {
                                                                             { SearchFor.Files, "Files" },
                                                                             {
                                                                                      SearchFor.FoldersAndFiles,
                                                                                      "Files and Directories"
                                                                             },
                                                                             { SearchFor.Folders, "Directories" }
                                                                    };

   public string SearchForText => SearchForChoices[SearchFor];

   public static IEnumerable SearchDepthList => new[] { "No Limit", "Top Directory", "Custom" };
   public static IEnumerable FileFormat      => new[] { "Excel (.xlsx)", "CSV comma (.csv)" };

   public string? SelectedDepthOption
   {
      get => _selectedDepthOption;
      set
      {
         DepthLevel = value switch
                      {
                               "No Limit"      => -1,
                               "Top Directory" => 1,
                               "Custom"        => 2,
                               _               => -1
                      };
         _selectedDepthOption = value;
         OnPropertyChanged();
      }
   }

   public RelayCommand SelectSearchFolderCommand { get; private set; }
   public RelayCommand SelectSaveFolderCommand   { get; private set; }
   public RelayCommand AddAccountCommand         { get; private set; }
   public RelayCommand RemoveAccountCommand      { get; private set; }

   public Brush StatusColor => CurrentState.ToLowerInvariant() switch
                               {
                                        "done"    => StatusBrushes.Success,
                                        "running" => StatusBrushes.Info,
                                        "queued"  => StatusBrushes.Neutral,
                                        "error"   => StatusBrushes.Danger,
                                        _         => StatusBrushes.Caution
                               };

   private void InitializeDirectories(string searchDirectory, string saveDirectory)
   {
      SearchDirectory = searchDirectory;
      SaveDirectory   = saveDirectory;
   }

   private void InitializeDepthSettings(int depthLevel)
   {
      SelectedDepthOption = depthLevel switch
                            {
                                     >= 2 => "Custom",
                                     1    => "Top Directory",
                                     _    => "No Limit"
                            };
      DepthLevel = depthLevel > 0 ? depthLevel : -1;
   }

   private void TimerStart()
   {
      Application.Current.Dispatcher.Invoke(() =>
      {
         if (_timer != null)
         {
            _timer.Stop();
            _timer.Tick -= TimerOnTick;
            _timer      =  null;
         }

         _timer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
                  {
                           Interval = TimeSpan.FromMilliseconds(100)
                  };

         _timer.Tick += TimerOnTick;

         _startTime = DateTime.Now;
         _backgroundTimer.Reset();
         _backgroundTimer.Start();
         _timer.Start();
      });
   }

   public void TimerStop()
   {
      _backgroundTimer.Stop();
      _timer?.Stop();
      _endTime = DateTime.Now;
      if (_timer == null) return;
      _timer.Tick -= TimerOnTick;
      _timer      =  null;
   }

   private void TimerOnTick(object? sender, EventArgs e)
   {
      var progress = Progress.Read();
      if (progress != _shownProgress)
      {
         _shownProgress = progress;
         OnPropertyChanged(nameof(ProgressText));
         OnPropertyChanged(nameof(CurrentFolder));
      }

      var elapsed = TimeSpan.FromSeconds(Math.Floor(_backgroundTimer.Elapsed.TotalSeconds));
      if (elapsed == _elapsedTime) return;
      _elapsedTime = elapsed;
      OnPropertyChanged(nameof(ElapsedTime));
      OnPropertyChanged(nameof(DetailedTimeInfo));
   }

   public SearchProgress Progress { get; } = new();

   private SearchProgress.Snapshot _shownProgress;

   public string ProgressText =>
            _shownProgress.Phase.Contains("Reading folders")
                     ? $"{_shownProgress.Phase} {_shownProgress.Items:N0} items in {_shownProgress.Folders:N0} folders"
                     : _shownProgress.Phase;

   public string CurrentFolder => _shownProgress.CurrentFolder ?? string.Empty;

   [MemberNotNull(nameof(SelectSearchFolderCommand), nameof(SelectSaveFolderCommand), nameof(AddAccountCommand),
                  nameof(RemoveAccountCommand))]
   private void InitializeCommands()
   {
      SelectSearchFolderCommand = new RelayCommand(_ =>
      {
         if (PickFolder() is { } folder) SearchDirectory = folder;
      });
      SelectSaveFolderCommand = new RelayCommand(_ =>
      {
         if (PickFolder() is { } folder) SaveDirectory = folder;
      });
      AddAccountCommand    = new RelayCommand(o => AddAccount((string)o));
      RemoveAccountCommand = new RelayCommand(RemoveAccount);
   }

   private static string? PickFolder()
   {
      OpenFolderDialog dialog = new() { ValidateNames = true };
      return dialog.ShowDialog() == true ? dialog.FolderName : null;
   }

   private void AddAccount(string parameter)
   {
      var account = parameter.Trim();
      if (account.Length > 0 && !ExcludedAccounts.Contains(account, StringComparer.OrdinalIgnoreCase))
         ExcludedAccounts.Add(account);
   }

   private void RemoveAccount(object parameter)
   {
      if (parameter is string account) ExcludedAccounts.Remove(account);
   }

   public SearchConfigViewModel Clone() => new()
                                           {
                                                    SearchDirectory     = SearchDirectory,
                                                    SaveDirectory       = SaveDirectory,
                                                    SearchFor           = SearchFor,
                                                    SelectedDepthOption = SelectedDepthOption,
                                                    DepthLevel          = DepthLevel,
                                                    IsLocalSearch       = IsLocalSearch,
                                                    Inherited           = Inherited,
                                                    ShowOwner           = ShowOwner,
                                                    ShowMatrix          = ShowMatrix,
                                                    ShowGroups          = ShowGroups,
                                                    StringExportAs      = StringExportAs,
                                                    OpenResult          = OpenResult,
                                                    ExcludedAccounts    = [.. ExcludedAccounts],
                                                    Domains             = [.. Domains]
                                           };
}