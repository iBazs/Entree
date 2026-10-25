// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using Wpf.Ui.Controls;
using Entree.Core.BaseClass;
using Entree.Services;

namespace Entree.MVVM.ViewModel.Control;

internal class MainViewModel : Core.BaseClass.ViewModel
{
   private static readonly (Type Page, string Label, SymbolRegular Icon)[] Pages =
   [
            (typeof(HomeViewModel), "Home", SymbolRegular.Home24),
            (typeof(CreateConfigViewModel), "Create Config", SymbolRegular.TextBulletListSquareEdit24),
            (typeof(SearchViewModel), "Search", SymbolRegular.Search24),
            (typeof(CompareViewModel), "Compare", SymbolRegular.BranchCompare24),
            (typeof(TaskViewModel), "Tasks", SymbolRegular.TaskListLtr24),
            (typeof(SettingsViewModel), "Settings", SymbolRegular.Settings24)
   ];

   public MainViewModel(INavigationService navigationService, LogViewModel logViewModel)
   {
      LogViewModel      = logViewModel;
      NavigationService = navigationService;
      var items = Pages.Select(page => new NavigationViewItem
                                       {
                                                Content = page.Label,
                                                Tag     = page.Page,
                                                Icon    = new SymbolIcon(page.Icon, 18),
                                                Command = new RelayCommand(_ => navigationService.NavigateTo(page.Page))
                                       })
                       .ToList();
      MenuItems   = [.. items.SkipLast(1)];
      FooterItems = [items[^1]];
      Highlight(items, navigationService.CurrentView);
      navigationService.PropertyChanged += (_, e) =>
      {
         if (e.PropertyName == nameof(INavigationService.CurrentView)) Highlight(items, navigationService.CurrentView);
      };
   }

   public ObservableCollection<object> FooterItems { get; }
   public ObservableCollection<object> MenuItems   { get; }

   public LogViewModel LogViewModel { get; }

   public INavigationService NavigationService { get; }

   private static void Highlight(IEnumerable<NavigationViewItem> items, Core.BaseClass.ViewModel? current)
   {
      foreach (var item in items)
         item.Dispatcher.Invoke(() => item.IsActive = (Type?)item.Tag == current?.GetType());
   }
}
