// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;

namespace Entree.Core.API;

internal sealed record TrustedDomain(string Name, string? NetBiosName, SecurityIdentifier Sid, bool IsPrimary);

internal sealed record DomainSource(SecureCredential Credential, SecurityIdentifier? Sid, string? NetBiosName);

public abstract record DomainContext
{
   private DomainContext() { }

   public sealed record Local(string Name, PrincipalContext Context) : DomainContext;

   public sealed record Server(string Name, PrincipalContext? Context) : DomainContext;

   public sealed record Domain(string Name, PrincipalContext Context, SecurityIdentifier? Sid, string? NetBiosName)
            : DomainContext;
}

internal static class DomainRouting
{
   internal const uint  DsDomainPrimary    = 0x8;
   private const uint   TrustTypeDownlevel = 1;
   private const uint   TrustTypeUplevel   = 2;
   private const string BuiltinSidPrefix   = "S-1-5-32-";

   private static readonly HashSet<string> LocalNames = new(StringComparer.OrdinalIgnoreCase) { ".", "localhost", "127.0.0.1" };

   public static IReadOnlyList<TrustedDomain> ToTrustedDomain(string? netBiosName, string? dnsName, uint flags,
                                                              uint trustType, SecurityIdentifier? sid)
   {
      if ((string.IsNullOrEmpty(dnsName) ? netBiosName : dnsName) is not { Length: > 0 } name || sid is null ||
          trustType is not (TrustTypeDownlevel or TrustTypeUplevel))
         return [];
      return [new TrustedDomain(name, netBiosName, sid, (flags & DsDomainPrimary) != 0)];
   }

   public static IReadOnlyList<DomainSource> Plan(IReadOnlyList<TrustedDomain> trusts,
                                                  IReadOnlyList<SecureCredential> configured, bool localOnly) =>
   [
            ..trusts.Where(t => !localOnly || t.IsPrimary)
                    .Select(t => new DomainSource(configured.FirstOrDefault(c => Matches(t, c.DomainPath)) ??
                                                  new SecureCredential { DomainPath = t.Name }, t.Sid, t.NetBiosName)),
            ..configured.Where(c => !localOnly && !trusts.Any(t => Matches(t, c.DomainPath)))
                        .Select(c => new DomainSource(c, null, null))
   ];

   private static bool Matches(TrustedDomain trust, string? path) =>
            string.Equals(path, trust.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, trust.NetBiosName, StringComparison.OrdinalIgnoreCase);

   public static string? ShareServer(string? path)
   {
      var unc = path switch
                {
                         null                                                                    => null,
                         _ when path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) => path[8..],
                         _ when path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                                path.StartsWith(@"\\.\", StringComparison.Ordinal)                => null,
                         _ when path.StartsWith(@"\\", StringComparison.Ordinal)                   => path[2..],
                         _                                                                       => null
                };
      var server = unc?.Split('\\')[0];
      return string.IsNullOrEmpty(server) || LocalNames.Contains(server) ||
             string.Equals(server.Split('.')[0], Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                      ? null
                      : server;
   }

   public static PrincipalContext? Directory(DomainContext domain) => domain switch
   {
            DomainContext.Local local   => local.Context,
            DomainContext.Server server => server.Context,
            DomainContext.Domain other  => other.Context,
            _                           => null
   };

   public static string Name(DomainContext domain) => domain switch
   {
            DomainContext.Local local   => local.Name,
            DomainContext.Server server => server.Name,
            DomainContext.Domain other  => other.Name,
            _                           => string.Empty
   };

   public static bool Serves(DomainContext domain, SecurityIdentifier sid, IReadOnlySet<SecurityIdentifier> domainSids) =>
            domain switch
            {
                     DomainContext.Local                => true,
                     DomainContext.Server               => IsBuiltin(sid) || sid.AccountDomainSid is { } owner && !domainSids.Contains(owner),
                     DomainContext.Domain { Sid: null } => true,
                     DomainContext.Domain other         => other.Sid == sid.AccountDomainSid,
                     _                                  => false
            };

   public static IEnumerable<DomainContext> GroupSources(IReadOnlyList<DomainContext> serving) =>
            serving.Any(d => d is DomainContext.Server) ? serving.Where(d => d is not DomainContext.Local) : serving;

   public static IReadOnlyList<AccountInfo> Merge(SecurityIdentifier sid,
                                                  IReadOnlyList<(DomainContext Source, AccountInfo Info)> answers)
   {
      var resolved = answers.Where(a => a.Info.Name is not null).ToList();
      if (resolved.Count == 0) return answers.Select(a => a.Info).Take(1).ToList();

      var remote = resolved.Where(a => a.Source is not DomainContext.Local).Select(a => a.Info).ToList();
      var local  = resolved.Where(a => a.Source is DomainContext.Local).Select(a => a.Info);
      if (remote.Count == 0) return local.ToList();

      var serverAnswered = sid.AccountDomainSid is not null && resolved.Any(a => a.Source is DomainContext.Server);
      return [..remote, ..local.Where(l => serverAnswered && !remote.Any(r => SameAccount(r, l)))];
   }

   private static bool SameAccount(AccountInfo a, AccountInfo b) =>
            string.Equals(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase);

   public static AccountState StateWithoutDirectory(SecurityIdentifier sid) =>
            sid.AccountDomainSid is not null || IsBuiltin(sid) ? AccountState.Limited : AccountState.Unknown;

   public static AccountType ToAccountType(SecurityIdentifier sid, int use) => use switch
   {
            1 or 9                        => AccountType.User,
            5 when IsServiceIdentity(sid) => AccountType.User,
            2 or 4 or 5                   => AccountType.Group,
            _                             => AccountType.UnIdentified
   };

   private static bool IsServiceIdentity(SecurityIdentifier sid) =>
            sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.LocalServiceSid) ||
            sid.IsWellKnown(WellKnownSidType.NetworkServiceSid);

   private static bool IsBuiltin(SecurityIdentifier sid) => sid.Value.StartsWith(BuiltinSidPrefix, StringComparison.Ordinal);
}
