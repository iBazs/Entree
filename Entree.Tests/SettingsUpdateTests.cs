// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Entree.MVVM.ViewModel.Control;
using Entree.Services;
using Entree.Tests.Fakes;
using Xunit;

namespace Entree.Tests;

public class SettingsUpdateTests
{
   private static SettingsViewModel NewViewModel(FakeUpdateService updates, FakeSearchService? search = null,
                                                 NotificationService? notifications = null) =>
            new(new FakeAppSettings(), new CredentialService(null), new LicenseService(),
                notifications ?? new NotificationService(), updates, search ?? new FakeSearchService(),
                new FakeUserDataService());

   [Fact]
   public void Portable_PointsToReleases()
   {
      var vm = NewViewModel(new FakeUpdateService { IsInstalled = false });

      Assert.Equal("Download new versions", vm.UpdateAction);
   }

   [Fact]
   public async Task Check_FindsNewerVersion_OffersInstall()
   {
      var notifications = new NotificationService();
      var vm = NewViewModel(new FakeUpdateService { IsInstalled = true, Available = "1.0.0" },
                            notifications: notifications);

      await vm.CheckForUpdatesAsync(true);

      Assert.Equal("Install 1.0.0 and restart", vm.UpdateAction);
      Assert.Equal("Update available", notifications.Current?.Title);
   }

   [Fact]
   public async Task Check_UpToDate_KeepsCheckAction()
   {
      var notifications = new NotificationService();
      var vm = NewViewModel(new FakeUpdateService { IsInstalled = true }, notifications: notifications);

      await vm.CheckForUpdatesAsync(true);

      Assert.Equal("Check for updates", vm.UpdateAction);
      Assert.Null(notifications.Current);
   }

   [Fact]
   public async Task Install_WhileSearchRuns_IsRefused()
   {
      var updates = new FakeUpdateService { IsInstalled = true, Available = "1.0.0" };
      var vm      = NewViewModel(updates, new FakeSearchService { IsSearchRunning = true });
      await vm.CheckForUpdatesAsync(true);

      vm.UpdateCommand.Execute(null);

      Assert.Equal(0, updates.Installs);
   }

   [Fact]
   public async Task Install_WhenIdle_DownloadsAndRestarts()
   {
      var updates = new FakeUpdateService { IsInstalled = true, Available = "1.0.0" };
      var vm      = NewViewModel(updates);
      var exits   = 0;
      vm.Exit = () => exits++;
      await vm.CheckForUpdatesAsync(true);

      vm.UpdateCommand.Execute(null);

      Assert.Equal(1, updates.Installs);
      Assert.Equal(1, exits);
   }

   [Fact]
   public async Task Install_WhenASearchStartsDuringDownload_Waits()
   {
      var search  = new FakeSearchService();
      var updates = new FakeUpdateService { IsInstalled = true, Available = "1.0.0", Downloading = () => search.IsSearchRunning = true };
      var vm      = NewViewModel(updates, search);
      var exits   = 0;
      vm.Exit = () => exits++;
      await vm.CheckForUpdatesAsync(true);

      vm.UpdateCommand.Execute(null);

      Assert.Equal(0, updates.Installs);
      Assert.Equal(0, exits);
   }
}
