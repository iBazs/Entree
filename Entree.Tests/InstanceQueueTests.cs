// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Entree.Core.BaseClass;
using Entree.Core.Controller;
using Entree.MVVM.ViewModel;
using Entree.Services;
using Xunit;

namespace Entree.Tests;

public class InstanceQueueTests
{
   [Theory]
   [InlineData(true)]
   [InlineData(true, "--search", "a.json")]
   [InlineData(true, "--silent", "--search", "a.json")]
   [InlineData(true, @"C:\Configs\acl.entree")]
   [InlineData(false, "--silent")]
   [InlineData(false, "--compare", "a.csv", "b.csv")]
   [InlineData(false, "--silent", "--compare", "a.csv", "b.csv")]
   public void IsInstanceRequest_ForwardsLaunchesSearchesAndConfigFiles(bool expected, params string[] args)
   {
      Assert.Equal(expected, StartupController.IsInstanceRequest(args));
   }

   [Fact]
   public void QueueKey_IgnoresCaseTrailingSeparatorAndExclusionOrder()
   {
      Assert.Equal(SearchService.QueueKey(Config(@"\\server\share\", ["B", "a"])),
                   SearchService.QueueKey(Config(@"\\SERVER\Share", ["a", "B"])));
   }

   [Fact]
   public void QueueKey_DiffersWhenAnOptionDiffers()
   {
      var plain  = Config(@"D:\Data", []);
      var matrix = Config(@"D:\Data", []);
      matrix.ShowMatrix = !plain.ShowMatrix;
      Assert.NotEqual(SearchService.QueueKey(plain), SearchService.QueueKey(matrix));
   }

   private static SearchConfigViewModel Config(string path, string[] excluded) =>
            new(path, @"C:\Reports", SearchFor.Folders, 1, excludedAccounts: [.. excluded]);
}
