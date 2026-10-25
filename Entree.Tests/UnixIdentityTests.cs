// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Entree.Core.BaseClass;
using Entree.Core.Helpers;
using Xunit;

namespace Entree.Tests;

public class UnixIdentityTests
{
   [Theory]
   [InlineData("S-1-22-1-1000", "Unix User", "uid 1000", AccountType.User)]
   [InlineData("S-1-22-2-100", "Unix Group", "gid 100", AccountType.Group)]
   [InlineData("S-1-5-88-1-0", "NFS User", "uid 0", AccountType.User)]
   [InlineData("S-1-5-88-2-27", "NFS Group", "gid 27", AccountType.Group)]
   [InlineData("S-1-5-88-3-493", "NFS Mode", "0755 rwxr-xr-x", AccountType.UnIdentified)]
   public void Classifies_SambaAndNfsSids(string sid, string domain, string name, AccountType type)
   {
      Assert.True(UnixIdentity.TryClassify(sid, out var label));
      Assert.Equal((domain, name, type), label);
   }

   [Theory]
   [InlineData("S-1-5-21-1-2-3-1001")]
   [InlineData("S-1-22-1")]
   [InlineData("S-1-22-1-abc")]
   [InlineData("S-1-22-3-5")]
   [InlineData("S-1-5-88-4-1")]
   [InlineData("S-1-5-32-544")]
   public void LeavesOtherSidsAlone(string sid) => Assert.False(UnixIdentity.TryClassify(sid, out _));

   [Theory]
   [InlineData(0u, "0000 ---------")]
   [InlineData(420u, "0644 rw-r--r--")]
   [InlineData(1023u, "1777 rwxrwxrwt")]
   [InlineData(2541u, "4755 rwsr-xr-x")]
   [InlineData(1512u, "2750 rwxr-s---")]
   [InlineData(2048u, "4000 --S------")]
   public void ModeText_MatchesLsNotation(uint mode, string expected) => Assert.Equal(expected, UnixIdentity.ModeText(mode));
}
