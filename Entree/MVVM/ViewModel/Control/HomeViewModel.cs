// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.ComponentModel;
using Entree.Core.BaseClass;
using Entree.Services;

namespace Entree.MVVM.ViewModel.Control;

public class HomeViewModel : Core.BaseClass.ViewModel
{
   private bool _isRunning;

   public HomeViewModel(ISearchService searchService, INavigationService navigationService)
   {
      NavigationService             =  navigationService;
      SearchService                 =  searchService;
      SearchService.PropertyChanged += SearchServiceOnPropertyChanged;
      NavigateToSearchCommand =
               new RelayCommand(_ => { NavigationService.NavigateTo<CreateConfigViewModel>(); });
      NavigateToSearchResultCommand =
               new RelayCommand(_ => { NavigationService.NavigateTo<SearchViewModel>(); });
      NavigateToSettingsCommand =
               new RelayCommand(_ => { NavigationService.NavigateTo<SettingsViewModel>(); });
      NavigateToCompareCommand =
               new RelayCommand(_ => { NavigationService.NavigateTo<CompareViewModel>(); });
      NavigateToTaskCommand = new RelayCommand(_ => { NavigationService.NavigateTo<TaskViewModel>(); });

      IsRunning = searchService.IsSearchRunning;
   }

   public RelayCommand NavigateToSearchCommand       { get; }
   public RelayCommand NavigateToCompareCommand      { get; }
   public RelayCommand NavigateToSearchResultCommand { get; }
   public RelayCommand NavigateToSettingsCommand     { get; }
   public RelayCommand NavigateToTaskCommand         { get; }

   private INavigationService NavigationService { get; }
   private ISearchService     SearchService     { get; }

   public bool IsRunning
   {
      get => _isRunning;
      private set => Set(ref _isRunning, value);
   }


   private void SearchServiceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
   {
      if (e.PropertyName != nameof(SearchService.IsSearchRunning)) return;
      IsRunning = SearchService.IsSearchRunning;
      OnPropertyChanged(nameof(IsRunning));
   }
}