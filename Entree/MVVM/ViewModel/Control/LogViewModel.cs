// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.ComponentModel;
using Serilog.Events;
using Entree.Core.BaseClass;
using Entree.Services;

namespace Entree.MVVM.ViewModel.Control;

public class LogViewModel : Core.BaseClass.ViewModel
{
   private bool          _isRunning;
   private Notification? _shownNotification;

   public LogViewModel(ISearchService searchService, LogViewerService logViewerService,
                       INotificationService notifications)
   {
      Notifications                 =  notifications;
      Notifications.PropertyChanged += NotificationsOnPropertyChanged;
      SearchService                 =  searchService;
      SearchService.PropertyChanged += SearchServiceOnPropertyChanged;
      LogEvents                     =  logViewerService.LogEvents;
      ClearLogCommand               =  new RelayCommand(_ => ClearLog());
   }

   private ISearchService                 SearchService   { get; }
   private INotificationService           Notifications   { get; }
   public  ObservableCollection<LogEvent> LogEvents       { get; }
   public  RelayCommand                   ClearLogCommand { get; }

   public bool IsNotificationOpen => Notifications.Current is not null;

   public Notification? ShownNotification
   {
      get => _shownNotification;
      private set => Set(ref _shownNotification, value);
   }

   public bool IsRunning
   {
      get => _isRunning;
      private set => Set(ref _isRunning, value);
   }

   public void DismissNotification() => Notifications.Dismiss();

   private void ClearLog() => LogEvents.Clear();

   private void NotificationsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
   {
      if (Notifications.Current is { } current) ShownNotification = current;
      OnPropertyChanged(nameof(IsNotificationOpen));
   }

   private void SearchServiceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
   {
      if (e.PropertyName != nameof(SearchService.IsSearchRunning)) return;
      IsRunning = SearchService.IsSearchRunning;
   }
}