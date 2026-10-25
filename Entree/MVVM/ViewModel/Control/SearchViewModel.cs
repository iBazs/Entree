// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Serilog;
using Entree.Core.BaseClass;
using Entree.Core.Helpers;
using Entree.Services;

namespace Entree.MVVM.ViewModel.Control;

public class SearchViewModel : Core.BaseClass.ViewModel
{
   // ReSharper disable AsyncVoidLambda
   public SearchViewModel(ISearchService searchService, INavigationService navigationService)
   {
      NewSearchCommand              =  new RelayCommand(_ => navigationService.NavigateTo<CreateConfigViewModel>());
      SearchService                 =  searchService;
      SearchConfigs                 =  searchService.SearchConfigs;
      SearchService.PropertyChanged += SearchServiceOnPropertyChanged;
      CancelAllSearchCommand        =  new RelayCommand(async _ => { await StopSearch(); });
      CancelSingleSearchCommand     =  new RelayCommand(async o => { await StopSearch(o); });
      ClearConfigsCommand           =  new RelayCommand(_ => ClearFinished());
      OpenDirectoryCommand = new RelayCommand(async o => { await OpenFile(o); },
                                              o => o is string path && !string.IsNullOrWhiteSpace(path));
   }

   public  ObservableCollection<SearchConfigViewModel> SearchConfigs { get; }
   private ISearchService                              SearchService { get; }
   private bool                                        IsCancelling  { get; set; }
   public  bool                                        IsRunning     => SearchService.IsSearchRunning;

   public RelayCommand CancelAllSearchCommand    { get; }
   public RelayCommand CancelSingleSearchCommand { get; }
   public RelayCommand OpenDirectoryCommand      { get; }
   public RelayCommand ClearConfigsCommand       { get; }
   public RelayCommand NewSearchCommand          { get; }


   private void ClearFinished()
   {
      foreach (var config in SearchConfigs.Where(config => config.IsFinished).ToList()) SearchConfigs.Remove(config);
   }

   private void SearchServiceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
   {
      if (e.PropertyName != nameof(SearchService.IsSearchRunning)) return;
      OnPropertyChanged(nameof(IsRunning));
   }

   private async Task StopSearch()
   {
      if (IsCancelling) return;
      try
      {
         IsCancelling = true;
         Log.Debug("Cancellation requested for all searches.");
         await Task.Run(() => SearchService.Stop());
      }
      catch (InvalidOperationException ex)
      {
         Log.Error("Failed to cancel searches - invalid operation: {Message}", ex.Message);
      }
      catch (Exception ex)
      {
         Log.Error("Error cancelling searches: {Type} - {Message}", ex.GetType().Name, ex.Message);
      }
      finally
      {
         IsCancelling = false;
      }
   }

   private async Task StopSearch(object id)
   {
      if (id is not Guid guidId || IsCancelling) return;
      try
      {
         IsCancelling = true;
         Log.Debug("Cancellation called.");
         await Task.Run(() => SearchService.Stop(guidId));
      }
      catch (Exception ex)
      {
         Log.Error("Error cancelling search {SearchId}: {Type} - {Message}", guidId, ex.GetType().Name, ex.Message);
      }
      finally
      {
         IsCancelling = false;
      }
   }

   private static async Task OpenFile(object? o)
   {
      if (o is not string path) return;
      var openTask = Task.Run(() => Directory.Exists(path) || File.Exists(path));
      if (await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromSeconds(3))) != openTask || !await openTask)
      {
         Log.Warning("Path is not reachable: {Path}", path);
         return;
      }

      PathHelper.OpenFile(path);
   }
}