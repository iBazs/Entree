// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Text.Json.Nodes;
using Entree.MVVM.ViewModel.Control;
using Entree.Services;
using Entree.Tests.Fakes;
using Xunit;

namespace Entree.Tests;

public sealed class UserDataTests : IDisposable
{
   private readonly string _root = Path.Combine(Path.GetTempPath(), "EntreeTests", Guid.NewGuid().ToString("N"));

   public void Dispose()
   {
      if (Directory.Exists(_root)) Directory.Delete(_root, true);
   }

   private string OldFolder(string logPath)
   {
      var folder = Path.Combine(_root, "old");
      Directory.CreateDirectory(Path.Combine(folder, "cache"));
      Directory.CreateDirectory(Path.Combine(folder, "Log"));
      File.WriteAllText(Path.Combine(folder, "cache", "k"), "key");
      File.WriteAllText(Path.Combine(folder, "appsettings.json"),
                        new JsonObject { ["AppLogPath"] = logPath, ["Theme"] = "Dark" }.ToJsonString());
      return folder;
   }

   private static string? SavedLogPath(string folder) =>
            JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "appsettings.json")))?["AppLogPath"]?.GetValue<string>();

   [Fact]
   public void Move_TakesSettingsKeyAndLogs_AndRepointsDefaultLogFolder()
   {
      var from = OldFolder(Path.Combine(DataMigration.LegacyDirectory, "Log"));
      var to   = Path.Combine(_root, "new");

      DataMigration.Move(from, to);

      Assert.True(File.Exists(Path.Combine(to, "cache", "k")));
      Assert.True(Directory.Exists(Path.Combine(to, "Log")));
      Assert.False(File.Exists(Path.Combine(from, "appsettings.json")));
      Assert.Equal(Path.Combine(to, "Log"), SavedLogPath(to));
   }

   [Fact]
   public void Move_KeepsCustomLogFolder()
   {
      var from = OldFolder(@"D:\Logs\Entree");
      var to   = Path.Combine(_root, "new");

      DataMigration.Move(from, to);

      Assert.Equal(@"D:\Logs\Entree", SavedLogPath(to));
   }

   [Fact]
   public void Move_NeverOverwritesExistingData()
   {
      var from = OldFolder(@"D:\Logs");
      var to   = Path.Combine(_root, "new");
      Directory.CreateDirectory(to);
      File.WriteAllText(Path.Combine(to, "appsettings.json"), "{}");

      DataMigration.Move(from, to);

      Assert.Equal("{}", File.ReadAllText(Path.Combine(to, "appsettings.json")));
      Assert.True(File.Exists(Path.Combine(from, "cache", "k")));
   }

   [Fact]
   public void VelopackRoot_IsRecognised()
   {
      var folder = Path.Combine(_root, "app");
      Directory.CreateDirectory(Path.Combine(folder, "current"));

      Assert.True(DataMigration.IsVelopackRoot(folder));
      Assert.False(DataMigration.IsVelopackRoot(_root));
   }

   [Fact]
   public async Task DeleteAll_RemovesTasksAndDataFolder()
   {
      var folder = OldFolder(@"D:\Logs");
      var tasks  = new FakeTaskSchedulerService();

      Assert.True(await new UserDataService(tasks, folder).DeleteAllAsync());
      Assert.False(Directory.Exists(folder));
   }

   [Fact]
   public async Task DeleteAll_KeepsDataWhenTasksRemain()
   {
      var folder = OldFolder(@"D:\Logs");
      var tasks  = new FakeTaskSchedulerService { DeleteAllSucceeds = false };

      Assert.False(await new UserDataService(tasks, folder).DeleteAllAsync());
      Assert.True(Directory.Exists(folder));
   }

   private static SettingsViewModel NewViewModel(FakeUserDataService userData, bool searching, bool confirm,
                                                 Action exit) =>
            new(new FakeAppSettings(), new CredentialService(null), new LicenseService(), new NotificationService(),
                new FakeUpdateService(), new FakeSearchService { IsSearchRunning = searching }, userData)
            {
                     ConfirmDeleteAll = () => Task.FromResult(confirm),
                     Exit             = exit
            };

   [Theory]
   [InlineData(true, true, 0, 0)]
   [InlineData(false, false, 0, 0)]
   [InlineData(false, true, 1, 1)]
   public void DeleteAllCommand_AsksAndExits(bool searching, bool confirm, int deletes, int exits)
   {
      var userData = new FakeUserDataService();
      var exited   = 0;

      NewViewModel(userData, searching, confirm, () => exited++).DeleteAllDataCommand.Execute(null);

      Assert.Equal(deletes, userData.Deletes);
      Assert.Equal(exits, exited);
   }
}
