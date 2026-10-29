// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.IO;
using System.Security.AccessControl;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Serilog;
using Wpf.Ui.Controls;
using Entree.Core.BaseClass;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.Services;

namespace Entree.MVVM.ViewModel.Control;

public class CompareViewModel : Core.BaseClass.ViewModel
{
   private const    int                                  MaxPreviewItems   = 100;
   private readonly ICompareService                      _compareService;
   private          string                               _controlButtonContent = "Compare";
   private          SymbolIcon                           _controlButtonIcon    = new(SymbolRegular.Play24);
   private          CancellationTokenSource              _cTokenSource;
   private          bool                                 _isComparable = true;
   private          bool                                 _isConsoleMode;
   private          bool                                 _isExportable;
   private          bool                                 _noDifferences;
   private          bool                                 _isRunning;
   private          string                               _newFilePath      = "";
   private          List<ComparedFileSystemStructResult> _newResults       = [];
   private          string                               _originalFilePath = "";
   private          string                               _outputFilePath   = "";

   private readonly INotificationService                 _notifications;

   public CompareViewModel(ICompareService compareService, INotificationService notifications)
   {
      _compareService = compareService;
      _notifications  = notifications;

      // ReSharper disable AsyncVoidLambda
      CompareCommand        = new RelayCommand(async _ => { await CompareAsync(); }, _ => CanCompare());
      BrowseOriginalCommand = new RelayCommand(_ => BrowseForFile(true));
      BrowseCompareCommand  = new RelayCommand(_ => BrowseForFile(false));
      OpenOutputCommand     = new RelayCommand(_ => OpenOutputFile(), _ => File.Exists(OutputFilePath));
      ExportResultsCommand = new RelayCommand(async o => { await ExportResultsAsync(o); },
                                              _ => ComparisonResults.Count > 0 && IsExportable);
      CancelCommand = new RelayCommand(async _ => await Cancel());
      ClearCommand  = new RelayCommand(async _ => await Clear(), _ => !IsRunning);
      _cTokenSource = new CancellationTokenSource();
      // ReSharper restore AsyncVoidLambda
   }

   public ObservableCollection<DifferenceResult> ComparisonResults { get; } = [];

   public bool IsRunning
   {
      get => _isRunning;
      set
      {
         if (!value)
         {
            ControlButtonContent = "Compare";
            ControlButtonIcon    = new SymbolIcon(SymbolRegular.Play24);
         }

         _isRunning = value;
         OnPropertyChanged();
         CommandManager.InvalidateRequerySuggested();
      }
   }

   private void Report(InfoBarSeverity severity, string message) =>
            _notifications.Show(severity, message, untilReplaced: message.EndsWith("...", StringComparison.Ordinal));

   public string OriginalFilePath
   {
      get => _originalFilePath;
      set
      {
         if (IsRunning || string.IsNullOrEmpty(value) || value == _originalFilePath) return;
         _originalFilePath = value;
         OnPropertyChanged();
         Clear();
      }
   }

   public string NewFilePath
   {
      get => _newFilePath;
      set
      {
         if (IsRunning || string.IsNullOrEmpty(value) || value == _newFilePath) return;
         _newFilePath = value;
         OnPropertyChanged();
         Clear();
      }
   }

   public string OutputFilePath
   {
      get => _outputFilePath;
      private set => Set(ref _outputFilePath, value);
   }

   public bool IsComparable
   {
      get => _isComparable;
      set => Set(ref _isComparable, value);
   }

   public bool NoDifferences
   {
      get => _noDifferences;
      private set => Set(ref _noDifferences, value);
   }

   public bool IsExportable
   {
      get => _isExportable;
      set => Set(ref _isExportable, value);
   }

   public string ControlButtonContent
   {
      get => _controlButtonContent;
      private set => Set(ref _controlButtonContent, value);
   }

   public SymbolIcon ControlButtonIcon
   {
      get => _controlButtonIcon;
      private set => Set(ref _controlButtonIcon, value);
   }

   public ICommand     CompareCommand        { get; }
   public ICommand     BrowseOriginalCommand { get; }
   public ICommand     BrowseCompareCommand  { get; }
   public ICommand     OpenOutputCommand     { get; }
   public ICommand     ExportResultsCommand  { get; }
   public RelayCommand CancelCommand         { get; }
   public RelayCommand ClearCommand          { get; }


   private Task Clear()
   {
      IsExportable  = false;
      IsComparable  = true;
      IsRunning     = false;
      NoDifferences = false;
      _compareService.Clear();

      _cTokenSource.Cancel();
      RenewToken();
      ComparisonResults.Clear();

      var hadResults = _newResults.Count > 0;
      _newResults = [];
      if (hadResults) _ = MemoryRelease.AfterLargeOperation();

      return Task.CompletedTask;
   }

   private CancellationToken RenewToken()
   {
      _cTokenSource.Dispose();
      _cTokenSource = new CancellationTokenSource();
      return _cTokenSource.Token;
   }

   private Task Cancel()
   {
      _cTokenSource.Cancel();
      return Task.CompletedTask;
   }

   private bool CanCompare() =>
            !IsRunning &&
            !IsExportable &&
            !string.IsNullOrEmpty(OriginalFilePath) &&
            !string.IsNullOrEmpty(NewFilePath) &&
            OriginalFilePath != NewFilePath &&
            File.Exists(OriginalFilePath) &&
            File.Exists(NewFilePath) &&
            ComparisonResults.Count == 0;

   private void BrowseForFile(bool isOriginal)
   {
      var dialog = new OpenFileDialog
                   {
                            Filter = "Entrée reports (*.xlsx;*.zip;*.csv)|*.xlsx;*.zip;*.csv",
                            Title  = $"Select {(isOriginal ? "Original" : "Compare")} file"
                   };

      if (dialog.ShowDialog() != true) return;
      if (isOriginal) OriginalFilePath = dialog.FileName;
      else NewFilePath                 = dialog.FileName;
   }

   private void OpenOutputFile() => PathHelper.OpenFile(OutputFilePath);

   public async Task CompareAsync()
   {
      try
      {
         await Clear();
         IsRunning            = true;
         Report(InfoBarSeverity.Informational, "Comparing files...");
         ControlButtonContent = "Comparing";
         ControlButtonIcon    = new SymbolIcon(SymbolRegular.BranchCompare24);

         if (_isConsoleMode) Application.Current.Dispatcher.Invoke(() => ComparisonResults.Clear());
         else ComparisonResults.Clear();

         _newResults = await _compareService.CompareFilesAsync(OriginalFilePath, NewFilePath, _cTokenSource.Token);
         if (_newResults.Count == 0)
         {
            Report(InfoBarSeverity.Informational, "No differences found.");
            Log.Information("No differences found.");
            IsComparable  = true;
            NoDifferences = true;
            return;
         }

         IsExportable = true;
         var groupedByPath = _newResults.GroupBy(r => r.Path).ToList();
         ProcessComparisonResults(groupedByPath.Take(MaxPreviewItems));
         Report(InfoBarSeverity.Success, groupedByPath.Count > MaxPreviewItems
                  ? $"{groupedByPath.Count:N0} paths with {_newResults.Count:N0} differences. The preview shows the first {MaxPreviewItems}; export for all."
                  : $"{groupedByPath.Count:N0} paths with {_newResults.Count:N0} differences.");
         IsComparable = false;
      }
      catch (OperationCanceledException)
      {
         Report(InfoBarSeverity.Warning, "Comparison was cancelled.");
         Log.Warning("Comparison was cancelled by user");
      }
      catch (FileNotFoundException ex)
      {
         var fileType = ex.FileName?.Contains(OriginalFilePath) == true ? "original" : "comparison";
         Report(InfoBarSeverity.Error, $"The {fileType} file could not be found. Please check if the file exists.");
         Log.Error(ex, "File not found during comparison: {error}", ex.Message);
      }
      catch (UnauthorizedAccessException ex)
      {
         Report(InfoBarSeverity.Error, "Access denied to one of the files. Check file permissions.");
         Log.Error(ex, "Access denied during comparison: {error}", ex.Message);
      }
      catch (NotSupportedException ex)
      {
         Report(InfoBarSeverity.Error, "Unsupported file format. Only .xlsx, .zip and .csv reports are supported.");
         Log.Error(ex, "Unsupported file format: {error}", ex.Message);
      }
      catch (FormatException ex)
      {
         Report(InfoBarSeverity.Error, ex.Message);
         Log.Error(ex, "Format error during comparison: {error}", ex.Message);
      }
      catch (OutOfMemoryException ex)
      {
         Report(InfoBarSeverity.Error, "Out of memory. Try using smaller input files.");
         Log.Error(ex, "Out of memory during comparison: {error}", ex.Message);
      }
      catch (IOException ex)
      {
         Report(InfoBarSeverity.Error, "Error accessing files. Files may be in use by another process.");
         Log.Error(ex, "IO error during comparison: {error}", ex.Message);
      }
      catch (Exception ex)
      {
         Report(InfoBarSeverity.Error, $"Error comparing files: {ex.Message}");
         Log.Error(ex, "Unexpected error comparing files: {Type} - {error}", ex.GetType().Name, ex.Message);
      }
      finally
      {
         IsRunning = false;
         _         = MemoryRelease.AfterLargeOperation();
      }
   }

   private void ProcessComparisonResults(IEnumerable<IGrouping<string, ComparedFileSystemStructResult>> groupedByPath)
   {
      foreach (var pathGroup in groupedByPath)
         ComparisonResults.Add(new DifferenceResult(pathGroup.Key, DifferenceTags(pathGroup)));
   }

   private static IReadOnlyList<DifferenceTag> DifferenceTags(IEnumerable<ComparedFileSystemStructResult> results) =>
            results.SelectMany(result => (result.Difference?.Split(", ", StringSplitOptions.RemoveEmptyEntries) ?? [])
                                        .Concat(result.AccessType == AccessControlType.Deny ? ["Deny"] : []))
                   .Distinct()
                   .Select(label => new DifferenceTag(label, TagBrush(label)))
                   .ToList();

   private static Brush TagBrush(string label) => label switch
                                                  {
                                                           "Missing" or "Removed Identity" or "Deny" => StatusBrushes.Danger,
                                                           "New Identity" or "New Access"             => StatusBrushes.Caution,
                                                           "New Owner"                                => StatusBrushes.Info,
                                                           "New"                                      => StatusBrushes.Success,
                                                           _                                          => StatusBrushes.Neutral
                                                  };

   public async Task ExportResultsAsync(object o)
   {
      if (o is not string fileType) return;
      var exportAsXlsx = fileType == "xlsx";
      try
      {
         IsRunning = true;
         Log.Information("Exporting results as {Format}...", fileType);
         Report(InfoBarSeverity.Informational, $"Exporting results as {fileType}...");
         ControlButtonContent = "Exporting";
         ControlButtonIcon    = new SymbolIcon(SymbolRegular.DrawerArrowDownload24);

         OutputFilePath = await _compareService.ExportResultsAsync(exportAsXlsx, RenewToken());
         if (string.IsNullOrEmpty(OutputFilePath))
         {
            Report(InfoBarSeverity.Informational, "No differences found to export.");
            Log.Information("No differences found to export.");
            return;
         }

         Log.Information("Results exported to {Output}", OutputFilePath);
         Report(InfoBarSeverity.Success, $"Results exported to {Path.GetFileName(OutputFilePath)}");
      }
      catch (OperationCanceledException)
      {
         Report(InfoBarSeverity.Warning, "Export was cancelled.");
         Log.Warning("Export was cancelled by user");
      }
      catch (UnauthorizedAccessException ex)
      {
         Report(InfoBarSeverity.Error, "Access denied when saving file. Check folder permissions.");
         Log.Error(ex, "Access denied during export: {error}", ex.Message);
      }
      catch (IOException ex)
      {
         Report(InfoBarSeverity.Error, "Error writing file. The file may be in use by another process.");
         Log.Error(ex, "IO error during export: {error}", ex.Message);
      }
      catch (OutOfMemoryException ex)
      {
         var suggestion = exportAsXlsx ? " Try exporting as CSV instead." : "";
         Report(InfoBarSeverity.Error, $"Not enough memory to complete the export.{suggestion}");
         Log.Error(ex, "Out of memory during export: {error}", ex.Message);
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error exporting result: {Type} - {error}", ex.GetType().Name, ex.Message);
         Report(InfoBarSeverity.Error, $"Error exporting results: {ex.Message}");
      }
      finally
      {
         IsRunning = false;
         _         = MemoryRelease.AfterLargeOperation();
      }
   }

   public void SetConsoleMode(bool b)
   {
      _isConsoleMode = b;
   }

   public sealed record DifferenceTag(string Label, Brush Background);

   public sealed record DifferenceResult(string Path, IReadOnlyList<DifferenceTag> Tags);
}