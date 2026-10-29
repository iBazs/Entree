// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Entree.Core.API;
using Entree.Core.BaseClass;
using Xunit;

namespace Entree.Tests;

public class DomainLookupTests
{
   [Theory]
   [InlineData("S-1-5-32-544", "BUILTIN", AccountType.Group)]
   [InlineData("S-1-5-32-545", "BUILTIN", AccountType.Group)]
   [InlineData("S-1-5-11", "NT AUTHORITY", AccountType.Group)]
   [InlineData("S-1-5-18", "NT AUTHORITY", AccountType.User)]
   [InlineData("S-1-5-19", "NT AUTHORITY", AccountType.User)]
   [InlineData("S-1-5-20", "NT AUTHORITY", AccountType.User)]
   public void Lookup_ReturnsTheTypeWindowsReports(string sid, string domain, AccountType type)
   {
      var account = DomainLookup.Lookup(new SecurityIdentifier(sid));

      Assert.Equal((domain, type), (account.Domain, account.Type));
      Assert.False(string.IsNullOrEmpty(account.Name));
      Assert.Null(account.Error);
   }

   [Fact]
   public void Lookup_Everyone_HasNoDomain()
   {
      var account = DomainLookup.Lookup(new SecurityIdentifier(WellKnownSidType.WorldSid, null));

      Assert.Null(account.Domain);
      Assert.Equal(AccountType.Group, account.Type);
   }

   [Fact]
   public void Lookup_CurrentUser_IsUser()
   {
      var sid = WindowsIdentity.GetCurrent().User!;

      var account = DomainLookup.Lookup(sid);

      Assert.Equal(AccountType.User, account.Type);
      Assert.Equal(Environment.UserName, account.Name, ignoreCase: true);
   }

   [Fact]
   public void Lookup_OnANamedServer_MatchesThisComputer()
   {
      var sid = WindowsIdentity.GetCurrent().User!;

      var remote = DomainLookup.Lookup(sid, Environment.MachineName);

      Assert.Equal(DomainLookup.Lookup(sid).FullName, remote.FullName);
      Assert.Equal(AccountState.Limited, remote.State);
   }

   [Fact]
   public void Lookup_UnknownSid_ReturnsError()
   {
      var account = DomainLookup.Lookup(new SecurityIdentifier("S-1-5-21-1-2-3-4242"));

      Assert.Equal(AccountType.UnIdentified, account.Type);
      Assert.Null(account.Name);
      Assert.NotNull(account.Error);
   }

   [Fact]
   public void Trusts_OnAnyMachine_ReturnsValidDomains()
   {
      switch (DomainLookup.Trusts())
      {
         case Outcome<IReadOnlyList<TrustedDomain>>.Done done:
            Assert.All(done.Value, t => Assert.False(string.IsNullOrEmpty(t.Name)));
            Assert.True(done.Value.Count(t => t.IsPrimary) <= 1);
            break;
         case Outcome<IReadOnlyList<TrustedDomain>>.Failed failed:
            Assert.False(string.IsNullOrEmpty(failed.Reason));
            break;
      }
   }

   [Theory]
   [InlineData(null)]
   [InlineData("relative\\folder")]
   [InlineData(@"\\fs01\share")]
   public void RemotePath_KeepsPathsWithoutADriveLetter(string? path) =>
            Assert.Equal(path, DomainLookup.RemotePath(path));

   [Fact]
   public void RemotePath_KeepsALocalDrive()
   {
      var system = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

      Assert.Equal(system, DomainLookup.RemotePath(system));
   }

   [Fact]
   public void RemotePath_MappedDrive_IsTheShare()
   {
      var share  = $@"\\localhost\{Path.GetPathRoot(Environment.SystemDirectory)![0]}$";
      var letter = Enumerable.Range('M', 14).Select(c => $"{(char)c}:")
                             .First(d => !DriveInfo.GetDrives().Any(x => x.Name.StartsWith(d, StringComparison.OrdinalIgnoreCase)));
      Assert.Equal(0, Net($"use {letter} {share}"));
      try
      {
         Assert.Equal($@"{share}\Windows", DomainLookup.RemotePath($@"{letter}\Windows"), ignoreCase: true);
         Assert.Null(DomainRouting.ShareServer(DomainLookup.RemotePath(letter)));
      }
      finally
      {
         Net($"use {letter} /delete /y");
      }
   }

   private static int Net(string arguments)
   {
      using var process = Process.Start(new ProcessStartInfo("net", arguments) { CreateNoWindow = true, UseShellExecute = false })!;
      process.WaitForExit();
      return process.ExitCode;
   }
}
