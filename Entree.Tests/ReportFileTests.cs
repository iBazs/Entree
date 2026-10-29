// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Immutable;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Xunit;

namespace Entree.Tests;

public sealed class ReportFileTests : IDisposable
{
   private static readonly SecurityIdentifier Readers = new("S-1-5-21-1-2-3-1001");
   private static readonly SecurityIdentifier Editors = new("S-1-5-21-1-2-3-1002");

   private readonly string _folder = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"report_test_{Guid.NewGuid():N}")).FullName;

   public void Dispose() => Directory.Delete(_folder, true);

   [Theory]
   [InlineData(@"\\srv\data", "data")]
   [InlineData(@"\\srv\data\", "data")]
   [InlineData(@"\\?\UNC\srv\data", "data")]
   [InlineData(@"D:\Data\", "Data")]
   [InlineData(@"D:\", "D")]
   [InlineData(@"\\?\", "Result")]
   [InlineData(null, "Result")]
   public void ReportName_IsTheLastFolderName(string? searchDirectory, string name) =>
            Assert.Equal(name, PathHelper.ReportName(searchDirectory));

   [Fact]
   public void ReserveUniqueFile_NeverReusesAnExistingFile()
   {
      var first = PathHelper.ReserveUniqueFile("data", ".xlsx", _folder);
      File.WriteAllText(first, "kept");

      var second = PathHelper.ReserveUniqueFile("data", ".xlsx", _folder);

      Assert.NotEqual(first, second);
      Assert.Equal("kept", File.ReadAllText(first));
      Assert.True(File.Exists(second));
   }

   [Fact]
   public void EnsureDirectoryExists_KeepsAFolderNameWithADot()
   {
      var folder = Path.Combine(_folder, "Reports.2025");

      Assert.Equal(folder, PathHelper.EnsureDirectoryExists(folder));
      Assert.True(Directory.Exists(folder));
   }

   [Fact]
   public void Matrix_ComparesFilesWithTheNearestListedFolder()
   {
      var root = @"\\srv\data";
      List<FileSystemStruct> entries =
      [
               Entry(root, true, Readers),
               Entry($@"{root}\a\b\same.txt", false, Readers),
               Entry($@"{root}\a\b\other.txt", false, Readers, Editors)
      ];

      var rows = AclMatrix.Create(entries, [Readers, Editors]).Select(r => r.Path);

      Assert.Equal([root, $@"{root}\a\b\other.txt"], rows);
   }

   private static FileSystemStruct Entry(string path, bool isDirectory, params SecurityIdentifier[] sids) =>
            new()
            {
                     Path                = path,
                     FileEntryAttributes = isDirectory ? FileAttributes.Directory : FileAttributes.Archive,
                     Security = SecurityInfo.Empty with
                     {
                              Allow = sids.ToImmutableDictionary(sid => sid, _ => FileSystemRights.ReadAndExecute)
                     }
            };
}
