// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using Entree.Services;
using Xunit;

namespace Entree.Tests;

public sealed class CredentialKeyTests : IDisposable
{
   private readonly string _directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"key_test_{Guid.NewGuid():N}")).FullName;

   private string KeyPath => Path.Combine(_directory, "k");

   public void Dispose() => Directory.Delete(_directory, true);

   [Fact]
   public void Key_IsCreatedOnceAndReloaded()
   {
      var created = CredentialKey.LoadOrCreate(KeyPath);

      Assert.NotNull(created);
      Assert.Equal(32, created.Length);
      Assert.Equal(created, CredentialKey.LoadOrCreate(KeyPath));
      Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
   }

   [Fact]
   public void Key_DiffersPerInstallation()
   {
      var other = Path.Combine(_directory, "k2");
      Assert.NotEqual(CredentialKey.LoadOrCreate(KeyPath), CredentialKey.LoadOrCreate(other));
   }

   [Fact]
   public void DamagedKey_IsNotReplaced()
   {
      File.WriteAllBytes(KeyPath, [1, 2, 3]);

      Assert.Null(CredentialKey.LoadOrCreate(KeyPath));
      Assert.Equal([1, 2, 3], File.ReadAllBytes(KeyPath));
   }

   [Fact]
   public async Task ConcurrentCreation_YieldsOneKey()
   {
      var keys = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => CredentialKey.LoadOrCreate(KeyPath))));

      Assert.All(keys, key => Assert.Equal(keys[0], key));
   }
}
