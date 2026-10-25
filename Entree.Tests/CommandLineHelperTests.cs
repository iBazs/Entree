// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Entree.Core.Helpers;
using Xunit;

namespace Entree.Tests;

public class CommandLineHelperTests
{
   [Theory]
   [InlineData("--search", true)]
   [InlineData("--silent", true)]
   [InlineData("-x", true)]
   [InlineData("/compare", true)]
   [InlineData("search", false)]
   [InlineData(@"C:\My Configs\acl.entree", false)]
   public void IsCommand_DetectsFlagPrefixes(string input, bool expected)
   {
      Assert.Equal(expected, CommandLineHelper.IsCommand(input));
   }

   [Theory]
   [InlineData("--search", "search")]
   [InlineData("--silent", "silent")]
   [InlineData("-s", "s")]
   [InlineData("/compare", "compare")]
   [InlineData("search", "search")]
   public void TrimCommand_StripsTheFlagPrefix(string input, string expected)
   {
      Assert.Equal(expected, CommandLineHelper.TrimCommand(input));
   }
}
