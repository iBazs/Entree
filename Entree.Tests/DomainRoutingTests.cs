// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
using Entree.Core.API;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;
using Xunit;

namespace Entree.Tests;

public sealed class DomainRoutingTests : IDisposable
{
   private static readonly SecurityIdentifier CorpSid = new("S-1-5-21-1-2-3");
   private static readonly SecurityIdentifier LabSid  = new("S-1-5-21-4-5-6");

   private static readonly TrustedDomain Corp = new("corp.example.com", "CORP", CorpSid, true);
   private static readonly TrustedDomain Lab  = new("lab.example.com", "LAB", LabSid, false);

   [Theory]
   [InlineData(1, AccountType.User)]
   [InlineData(9, AccountType.User)]
   [InlineData(2, AccountType.Group)]
   [InlineData(4, AccountType.Group)]
   [InlineData(5, AccountType.Group)]
   [InlineData(3, AccountType.UnIdentified)]
   [InlineData(6, AccountType.UnIdentified)]
   [InlineData(8, AccountType.UnIdentified)]
   [InlineData(10, AccountType.UnIdentified)]
   public void ToAccountType_MapsSidNameUse(int use, AccountType type) =>
            Assert.Equal(type, DomainRouting.ToAccountType(new SecurityIdentifier("S-1-5-21-1-2-3-1001"), use));

   [Theory]
   [InlineData("CORP", "corp.example.com", 0x8u, 2u, "corp.example.com", true)]
   [InlineData("OLD", null, 0x2u, 1u, "OLD", false)]
   [InlineData("OLD", "", 0x2u, 1u, "OLD", false)]
   public void ToTrustedDomain_KeepsWindowsDomains(string? netBios, string? dns, uint flags, uint trustType,
                                                   string name, bool isPrimary) =>
            Assert.Equal(new TrustedDomain(name, netBios, CorpSid, isPrimary),
                         Assert.Single(DomainRouting.ToTrustedDomain(netBios, dns, flags, trustType, CorpSid)));

   [Theory]
   [InlineData("REALM", "realm.example.com", 3u, true)]
   [InlineData("CORP", "corp.example.com", 2u, false)]
   [InlineData(null, null, 2u, true)]
   public void ToTrustedDomain_DropsUnusableEntries(string? netBios, string? dns, uint trustType, bool hasSid) =>
            Assert.Empty(DomainRouting.ToTrustedDomain(netBios, dns, 0x2, trustType, hasSid ? CorpSid : null));

   [Fact]
   public void Plan_UsesTrustsWithoutStoredAccounts()
   {
      var plan = DomainRouting.Plan([Corp, Lab], [], false);

      Assert.Equal([("corp.example.com", "", CorpSid), ("lab.example.com", "", LabSid)],
                   plan.Select(s => (s.Credential.DomainPath, s.Credential.Username, s.Sid)));
   }

   [Theory]
   [InlineData("lab.example.com")]
   [InlineData("lab")]
   public void Plan_SettingsOverrideAMatchingTrust(string path)
   {
      var configured = new SecureCredential { DomainPath = path, Username = @"LAB\reader" };

      var plan = DomainRouting.Plan([Corp, Lab], [configured], false);

      Assert.Equal(2, plan.Count);
      Assert.Same(configured, plan[1].Credential);
      Assert.Equal(LabSid, plan[1].Sid);
   }

   [Fact]
   public void Plan_KeepsUntrustedSettingsDomains()
   {
      var isolated = new SecureCredential { DomainPath = "10.0.0.5" };

      var plan = DomainRouting.Plan([Corp], [isolated], false);

      Assert.Same(isolated, plan[1].Credential);
      Assert.Null(plan[1].Sid);
   }

   [Fact]
   public void Plan_LocalOnly_KeepsThePrimaryDomainAndItsOverride()
   {
      var corp     = new SecureCredential { DomainPath = "CORP", Username = @"CORP\reader" };
      var isolated = new SecureCredential { DomainPath = "10.0.0.5" };

      var plan = DomainRouting.Plan([Corp, Lab], [corp, isolated], true);

      Assert.Same(corp, Assert.Single(plan).Credential);
   }

   private static readonly HashSet<SecurityIdentifier> KnownDomains = [CorpSid, LabSid];

   private readonly PrincipalContext _machine = new(ContextType.Machine);

   public void Dispose() => _machine.Dispose();

   private DomainContext Local  => new DomainContext.Local("PC01", _machine);
   private DomainContext Server => new DomainContext.Server("FS01", null);

   private DomainContext Domain(SecurityIdentifier? sid) => new DomainContext.Domain("corp", _machine, sid, "CORP");

   private DomainContext Context(string kind) => kind switch
   {
            "Local"  => Local,
            "Server" => Server,
            _        => Domain(CorpSid)
   };

   [Theory]
   [InlineData("Domain", "S-1-5-21-1-2-3-1001", true)]
   [InlineData("Domain", "S-1-5-21-4-5-6-1001", false)]
   [InlineData("Domain", "S-1-5-32-544", false)]
   [InlineData("Server", "S-1-5-32-544", true)]
   [InlineData("Server", "S-1-5-21-7-8-9-1001", true)]
   [InlineData("Server", "S-1-5-21-1-2-3-1001", false)]
   [InlineData("Server", "S-1-1-0", false)]
   [InlineData("Local", "S-1-5-21-1-2-3-1001", true)]
   public void Serves_RoutesEachSidToItsOwner(string kind, string sid, bool serves) =>
            Assert.Equal(serves, DomainRouting.Serves(Context(kind),
                                                     new SecurityIdentifier(sid), KnownDomains));

   [Fact]
   public void Serves_SettingsDomainWithoutSid_ServesAll() =>
            Assert.True(DomainRouting.Serves(Domain(null), new SecurityIdentifier("S-1-5-32-544"),
                                            KnownDomains));

   [Fact]
   public void GroupSources_WithShareServer_SkipsThisComputer()
   {
      DomainContext[] serving = [Server, Local];

      Assert.Equal([serving[0]], DomainRouting.GroupSources(serving));
      Assert.Equal([serving[1]], DomainRouting.GroupSources([serving[1]]));
   }

   [Theory]
   [InlineData(@"\\fs01\share\data", "fs01")]
   [InlineData(@"\\fs01.corp.example.com\share", "fs01.corp.example.com")]
   [InlineData(@"\\?\UNC\fs01\share", "fs01")]
   [InlineData(@"\\10.0.0.5\share", "10.0.0.5")]
   [InlineData(@"\\localhost\c$", null)]
   [InlineData(@"\\.\c:\data", null)]
   [InlineData(@"\\?\C:\data", null)]
   [InlineData(@"C:\data", null)]
   [InlineData(null, null)]
   public void ShareServer_ReadsTheServerOfUncPaths(string? path, string? server) =>
            Assert.Equal(server, DomainRouting.ShareServer(path));

   [Fact]
   public void ShareServer_ThisComputer_IsNotRemote() =>
            Assert.Null(DomainRouting.ShareServer($@"\\{Environment.MachineName}\share"));

   private static readonly SecurityIdentifier MachineLocal = new("S-1-5-21-7-8-9-500");

   private static AccountInfo Named(SecurityIdentifier sid, string domain, string name) =>
            new(sid, domain, name, AccountType.User);

   [Fact]
   public void Merge_ClonedSid_ShowsBothAccounts()
   {
      var server = Named(MachineLocal, "FS01", "Administrator");
      var local  = Named(MachineLocal, "PC01", "Administrator");

      var merged = DomainRouting.Merge(MachineLocal, [(Server, server), (Local, local)]);

      Assert.Equal([server, local], merged);
   }

   [Fact]
   public void Merge_SameAnswer_ShowsOne()
   {
      var server = Named(MachineLocal, "FS01", "Administrator");

      var merged = DomainRouting.Merge(MachineLocal,
                                      [(Server, server), (Local, Named(MachineLocal, "fs01", "administrator"))]);

      Assert.Same(server, Assert.Single(merged));
   }

   [Fact]
   public void Merge_ServerOnly_HidesUnresolvedLocalAnswer()
   {
      var server = Named(MachineLocal, "FS01", "Administrator");

      var merged = DomainRouting.Merge(MachineLocal,
                                      [(Server, server), (Local, new AccountInfo(MachineLocal, error: "none"))]);

      Assert.Same(server, Assert.Single(merged));
   }

   [Fact]
   public void Merge_BuiltinFromServer_WinsOverLocalLanguage()
   {
      var builtin = new SecurityIdentifier("S-1-5-32-544");
      var server  = Named(builtin, "VORDEFINIERT", "Administratoren");

      var merged = DomainRouting.Merge(builtin, [(Server, server), (Local, Named(builtin, "BUILTIN", "Administrators"))]);

      Assert.Same(server, Assert.Single(merged));
   }

   [Fact]
   public void Merge_DomainAnswer_DropsLocalTranslation()
   {
      var sid    = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
      var domain = Named(sid, "CORP", "bob");

      Assert.Same(domain, Assert.Single(DomainRouting.Merge(sid, [(Domain(CorpSid), domain), (Local, Named(sid, "CORP", "bob"))])));
   }

   [Fact]
   public void Merge_SeveralDomains_ShowsAll()
   {
      var sid = new SecurityIdentifier("S-1-5-21-1-2-3-1001");

      Assert.Equal(2, DomainRouting.Merge(sid, [(Domain(CorpSid), Named(sid, "A", "bob")), (Domain(CorpSid), Named(sid, "B", "eve"))]).Count);
   }

   [Fact]
   public void Merge_NothingResolved_KeepsTheError()
   {
      var error = new AccountInfo(MachineLocal, error: "none");

      Assert.Same(error, Assert.Single(DomainRouting.Merge(MachineLocal, [(Local, error)])));
   }

   [Theory]
   [InlineData("S-1-5-21-1-2-3-1001", AccountState.Limited)]
   [InlineData("S-1-5-32-544", AccountState.Limited)]
   [InlineData("S-1-1-0", AccountState.Unknown)]
   [InlineData("S-1-5-18", AccountState.Unknown)]
   public void StateWithoutDirectory_MarksAccountsWhoseDetailsAreMissing(string sid, AccountState state) =>
            Assert.Equal(state, DomainRouting.StateWithoutDirectory(new SecurityIdentifier(sid)));
}
