// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using Entree.MVVM.Model;
using Entree.Services;
using Xunit;

namespace Entree.Tests;

public sealed class CredentialServiceTests : IDisposable
{
   private readonly string _directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"cred_test_{Guid.NewGuid():N}")).FullName;

   private string StorePath => Path.Combine(_directory, "d");

   public void Dispose() => Directory.Delete(_directory, true);

   private static SecureCredential Domain(string path) => new() { DomainPath = path };

   [Fact]
   public async Task AddedCredentials_SurviveReload()
   {
      var service = new CredentialService(StorePath);
      Assert.True(await service.AddCredentialAsync(Domain("corp.local")));
      Assert.True(await service.AddCredentialAsync(Domain("lab.local")));

      var reloaded = new CredentialService(StorePath).GetObservableCredentials();

      Assert.Equal(["corp.local", "lab.local"], reloaded.Select(c => c.DomainPath));
   }

   [Fact]
   public async Task AddingSameDomain_ReplacesInsteadOfDuplicating()
   {
      var service = new CredentialService(StorePath);
      await service.AddCredentialAsync(Domain("corp.local"));
      await service.AddCredentialAsync(Domain("CORP.local"));

      Assert.Single(new CredentialService(StorePath).GetObservableCredentials());
   }

   [Fact]
   public async Task CorruptMainFile_RestoresFromBackupAndKeepsCopy()
   {
      var service = new CredentialService(StorePath);
      await service.AddCredentialAsync(Domain("corp.local"));
      await service.AddCredentialAsync(Domain("lab.local"));
      File.WriteAllText(StorePath, "{ torn wri");

      var reloaded = new CredentialService(StorePath).GetObservableCredentials();

      Assert.Equal(["corp.local", "lab.local"], reloaded.Select(c => c.DomainPath));
      Assert.Single(Directory.GetFiles(_directory, "d.corrupt-*"));
   }

   [Fact]
   public async Task RemovedCredential_DoesNotReturnFromBackup()
   {
      var service = new CredentialService(StorePath);
      await service.AddCredentialAsync(Domain("corp.local"));
      await service.AddCredentialAsync(Domain("lab.local"));
      await service.RemoveCredentialAsync(service.GetObservableCredentials().First(c => c.DomainPath == "lab.local"));
      File.WriteAllText(StorePath, "{ torn wri");

      var reloaded = new CredentialService(StorePath).GetObservableCredentials();

      Assert.Equal(["corp.local"], reloaded.Select(c => c.DomainPath));
   }

   [Fact]
   public void NullAndBlankEntries_AreIgnored()
   {
      File.WriteAllText(StorePath, """[null,{"D":""},{"D":"corp.local","U":"","P":""}]""");

      Assert.Equal(["corp.local"], new CredentialService(StorePath).GetObservableCredentials().Select(c => c.DomainPath));
   }

   [Fact]
   public async Task FailedSave_LeavesMemoryAndDiskUnchanged()
   {
      var service = new CredentialService(StorePath);
      await service.AddCredentialAsync(Domain("corp.local"));
      Directory.CreateDirectory(Path.Combine(_directory, "blocked"));
      var blocked = new CredentialService(Path.Combine(_directory, "blocked"));

      Assert.False(await blocked.AddCredentialAsync(Domain("lab.local")));
      Assert.Empty(blocked.GetObservableCredentials());
   }

   [Fact]
   public async Task NoTemporaryFilesRemainAfterSaving()
   {
      var service = new CredentialService(StorePath);
      await Task.WhenAll(Enumerable.Range(0, 20).Select(i => service.AddCredentialAsync(Domain($"d{i}.local"))));

      Assert.Equal(20, new CredentialService(StorePath).GetObservableCredentials().Count);
      Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
   }

   [Fact]
   public void UnreadableFile_KeepsOnlyTheNewestCorruptCopies()
   {
      File.WriteAllText(StorePath, "not json");
      foreach (var stamp in new[] { "20200101000000", "20210101000000", "20220101000000", "20230101000000" })
         File.WriteAllText($"{StorePath}.corrupt-{stamp}", "old");

      _ = new CredentialService(StorePath);

      var kept = CredentialService.CorruptCopies(StorePath).Select(Path.GetFileName).ToList();
      Assert.Equal(3, kept.Count);
      Assert.DoesNotContain("d.corrupt-20200101000000", kept);
      Assert.DoesNotContain("d.corrupt-20210101000000", kept);
   }
}
