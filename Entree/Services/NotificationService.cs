// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Wpf.Ui.Controls;
using Entree.Core.BaseClass;

namespace Entree.Services;

public sealed record Notification(InfoBarSeverity Severity, string Title, string Message);

public interface INotificationService : System.ComponentModel.INotifyPropertyChanged
{
   Notification? Current { get; }
   void          Show(InfoBarSeverity severity, string message, string title = "", bool untilReplaced = false);
   void          Dismiss();
}

public sealed class NotificationService : ObservableObject, INotificationService
{
   private Notification?            _current;
   private CancellationTokenSource? _lifetime;

   public Notification? Current
   {
      get => _current;
      private set => Set(ref _current, value);
   }

   public void Show(InfoBarSeverity severity, string message, string title = "", bool untilReplaced = false) =>
            Replace(new Notification(severity, title, message), untilReplaced ? null : Lifetime(severity));

   public void Dismiss() => Replace(null, null);

   internal void Replace(Notification? notification, TimeSpan? lifetime)
   {
      _lifetime?.Cancel();
      _lifetime = null;
      Current   = notification;
      if (notification is null || lifetime is null) return;
      _lifetime = new CancellationTokenSource();
      _         = ExpireAsync(notification, lifetime.Value, _lifetime.Token);
   }

   internal static TimeSpan Lifetime(InfoBarSeverity severity) =>
            TimeSpan.FromSeconds(severity switch
                                 {
                                          InfoBarSeverity.Error   => 15,
                                          InfoBarSeverity.Warning => 10,
                                          _                       => 6
                                 });

   private async Task ExpireAsync(Notification notification, TimeSpan lifetime, CancellationToken token)
   {
      try
      {
         await Task.Delay(lifetime, token);
      }
      catch (OperationCanceledException)
      {
         return;
      }

      if (ReferenceEquals(Current, notification)) Current = null;
   }
}
