// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Wpf.Ui.Controls;
using Entree.Services;
using Xunit;

namespace Entree.Tests;

public class NotificationServiceTests
{
   [Fact]
   public async Task Notification_ExpiresAfterItsLifetime()
   {
      var service = new NotificationService();
      service.Replace(new Notification(InfoBarSeverity.Success, "", "done"), TimeSpan.FromMilliseconds(50));
      Assert.NotNull(service.Current);
      await Task.Delay(400);
      Assert.Null(service.Current);
   }

   [Fact]
   public async Task OlderLifetime_DoesNotHideANewerNotification()
   {
      var service = new NotificationService();
      service.Replace(new Notification(InfoBarSeverity.Success, "", "first"), TimeSpan.FromMilliseconds(50));
      service.Show(InfoBarSeverity.Informational, "Comparing files...", untilReplaced: true);
      await Task.Delay(400);
      Assert.Equal("Comparing files...", service.Current?.Message);
   }

   [Fact]
   public void Dismiss_HidesAtOnce()
   {
      var service = new NotificationService();
      service.Show(InfoBarSeverity.Error, "failed");
      service.Dismiss();
      Assert.Null(service.Current);
   }

   [Theory]
   [InlineData(InfoBarSeverity.Error, 15)]
   [InlineData(InfoBarSeverity.Warning, 10)]
   [InlineData(InfoBarSeverity.Success, 6)]
   [InlineData(InfoBarSeverity.Informational, 6)]
   public void Lifetime_DependsOnSeverity(InfoBarSeverity severity, int seconds) =>
      Assert.Equal(TimeSpan.FromSeconds(seconds), NotificationService.Lifetime(severity));
}
