// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Net;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Serilog;
using Entree.Core.BaseClass;
using Entree.Core.Helpers;
using Entree.MVVM.Model;

namespace Entree.Core.API;

public sealed class SecurityApi : IDisposable
{
   private readonly ParallelOptions _parallelOptions;

   public SecurityApi(IEnumerable<DomainContext> domains, ParallelOptions parallelOptions)
   {
      _parallelOptions = parallelOptions ?? throw new ArgumentNullException(nameof(parallelOptions));
      Domains = [..domains ?? throw new ArgumentNullException(nameof(domains)),
                 new DomainContext.Local(Environment.MachineName, new PrincipalContext(ContextType.Machine))];
      DomainSids = Domains.OfType<DomainContext.Domain>().Select(d => d.Sid).OfType<SecurityIdentifier>().ToFrozenSet();

      foreach (var domain in Domains.OfType<DomainContext.Domain>())
         NetBiosName.TryAdd(domain.Context, domain.NetBiosName ?? GetDomainNetBiosName(domain.Context));
      foreach (var server in Domains.OfType<DomainContext.Server>())
         if (server.Context is { } context)
            NetBiosName.TryAdd(context, GetDomainNetBiosName(context));
   }

   private ImmutableArray<DomainContext>  Domains    { get; }
   private FrozenSet<SecurityIdentifier>  DomainSids { get; }
   private ConcurrentDictionary<SecurityIdentifier, bool> GroupsDone { get; } = [];
   private ConcurrentDictionary<SecurityIdentifier, ConcurrentBag<AccountInfo>> AccountCache { get; } = [];
   private ConcurrentDictionary<SecurityIdentifier, bool> SidValues { get; } = [];
   private ConcurrentDictionary<PrincipalContext, string> NetBiosName { get; } = [];
   private ConcurrentDictionary<DomainContext, bool>      Unreadable { get; } = [];
   private ConcurrentDictionary<DescriptorKey, SecurityInfo> DescriptorCache { get; } = [];

   private const int MaxCachedDescriptors = 100_000;

   public void Dispose()
   {
      foreach (var domain in Domains) DomainRouting.Directory(domain)?.Dispose();
   }

   public IEnumerable<SecurityIdentifier> GetSecurityIdentifiers() => SidValues.Keys;

   public FileSystemStruct GetFullSecurity(string path, FileAttributes fileAttributes,
                                           DateTime date, bool isOwner, bool isInherited)
   {
      ArgumentNullException.ThrowIfNull(path);

      return new FileSystemStruct
             {
                      Path                = path,
                      FileEntryAttributes = fileAttributes,
                      LastWriteTime       = date,
                      Security            = ReadSecurity(path, fileAttributes.HasFlag(FileAttributes.Directory), isOwner,
                                                         isInherited)
             };
   }

   private SecurityInfo ReadSecurity(string path, bool isDirectory, bool isOwner, bool isInherited)
   {
      var (descriptor, error) = SecurityDescriptorReader.Read(path, isOwner);
      if (descriptor is null) return SecurityInfo.Failed(GetErrorType(SecurityDescriptorReader.ToException(error)));

      var key = new DescriptorKey(descriptor, isDirectory);
      if (DescriptorCache.TryGetValue(key, out var cached)) return cached;

      var parsed = ParseSecurity(descriptor, isDirectory, isOwner, isInherited, path);
      if (DescriptorCache.Count < MaxCachedDescriptors) DescriptorCache.TryAdd(key, parsed);
      return parsed;
   }

   private SecurityInfo ParseSecurity(byte[] descriptor, bool isDirectory, bool isOwner, bool isInherited,
                                        string path)
   {
      try
      {
         FileSystemSecurity security = isDirectory ? new DirectorySecurity() : new FileSecurity();
         security.SetSecurityDescriptorBinaryForm(descriptor,
                                                  isOwner
                                                           ? AccessControlSections.Access | AccessControlSections.Owner
                                                           : AccessControlSections.Access);
         var (allow, deny) = GetAccessRules(security, isInherited);
         return new SecurityInfo(isOwner ? GetOwner(security) as SecurityIdentifier : null, allow, deny, null);
      }
      catch (Exception ex)
      {
         Log.Warning("Failed to parse security information for {Path}: {Message}", path, ex.Message);
         return SecurityInfo.Failed(GetErrorType(ex));
      }
   }

   private readonly struct DescriptorKey : IEquatable<DescriptorKey>
   {
      private readonly byte[] _descriptor;
      private readonly bool   _isDirectory;
      private readonly int    _hash;

      public DescriptorKey(byte[] descriptor, bool isDirectory)
      {
         _descriptor  = descriptor;
         _isDirectory = isDirectory;
         var hash = new HashCode();
         hash.AddBytes(descriptor);
         hash.Add(isDirectory);
         _hash = hash.ToHashCode();
      }

      public bool Equals(DescriptorKey other) =>
               _isDirectory == other._isDirectory && _descriptor.AsSpan().SequenceEqual(other._descriptor);

      public override bool Equals(object? obj) => obj is DescriptorKey other && Equals(other);
      public override int  GetHashCode()       => _hash;
   }

   private List<AccountInfo[]> ProcessGroupMembers(GroupPrincipal group, AccountInfo account)
   {
      var nested   = new List<AccountInfo[]>();
      var members  = new List<AccountInfo>();
      var complete = true;
      try
      {
         using var enumerator = group.Members.GetEnumerator();
         while (enumerator.MoveNext())
         {
            _parallelOptions.CancellationToken.ThrowIfCancellationRequested();
            using var member = enumerator.Current;
            var info = member.Sid is { } sid ? GetAccountInfos(sid)[0] : CreateAccountInfo(member);
            members.Add(info);
            if (info.Type == AccountType.Group) nested.AddRange(GroupExplore(info));
         }
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
         complete = false;
         Log.Warning("Could not read every member of {GroupName}: {Message}", account.FullName, ex.Message);
      }

      if (!complete) MarkLimited(account);
      nested.Add([..members, account]);
      return nested;
   }

   private static void MarkLimited(AccountInfo account)
   {
      if (account.State == AccountState.Enabled) account.State = AccountState.Limited;
   }

   private IdentityReference? GetOwner(FileSystemSecurity fss)
   {
      try
      {
         var ownerIdentity = GetOwnerIdentity(fss);
         if (ownerIdentity is SecurityIdentifier sid) SidValues.TryAdd(sid, true);

         return ownerIdentity;
      }
      catch (Exception ex)
      {
         Log.Warning("Failed to get owner: {Message}", ex.Message);
         return null;
      }
   }

   private (ImmutableDictionary<SecurityIdentifier, FileSystemRights> Allow,
            ImmutableDictionary<SecurityIdentifier, FileSystemRights> Deny) GetAccessRules(
            FileSystemSecurity fss, bool isInherited, Type? currentAccountType = null)
   {
      currentAccountType ??= typeof(SecurityIdentifier);

      try
      {
         var accessRules = AppliedRules(FileSystemGetAccessControl(fss, isInherited, currentAccountType));

         foreach (var rule in accessRules)
            if (rule.IdentityReference is SecurityIdentifier sid)
               SidValues.TryAdd(sid, true);

         return (ProcessAccessRules(accessRules, AccessControlType.Allow),
                 ProcessAccessRules(accessRules, AccessControlType.Deny));
      }
      catch (Exception ex)
      {
         Log.Warning("Failed to get access rules: {Message}", ex.Message);
         return (ImmutableDictionary<SecurityIdentifier, FileSystemRights>.Empty,
                 ImmutableDictionary<SecurityIdentifier, FileSystemRights>.Empty);
      }
   }

   public IEnumerable<AccountInfo[]> GetGroupMembers(AccountInfo accountInfo) =>
            accountInfo.Type == AccountType.Group ? GroupExplore(accountInfo) : [];

   private IReadOnlyList<AccountInfo[]> GroupExplore(AccountInfo account)
   {
      if (string.IsNullOrEmpty(account.Name) || string.IsNullOrEmpty(account.Domain))
         return [];
      if (UnixIdentity.IsUnix(account.Sid)) return [[account]];

      if (CleanAccountName(account.Name, out var name, out var domain))
      {
         account.Name   = name;
         account.Domain = domain;
      }

      if (account.Name == null || !GroupsDone.TryAdd(account.Sid, true))
         return [];

      using var group = FindGroup(account);
      if (group is not null) return ProcessGroupMembers(group, account);
      MarkLimited(account);
      return [[account]];
   }

   private GroupPrincipal? FindGroup(AccountInfo account)
   {
      foreach (var principalContext in DomainRouting.GroupSources(Serving(account.Sid))
                                                 .Where(d => !Unreadable.ContainsKey(d))
                                                 .Select(DomainRouting.Directory)
                                                 .OfType<PrincipalContext>())
      {
         try
         {
            var group = GroupPrincipal.FindByIdentity(principalContext, IdentityType.Sid, account.Sid.Value);
            if (group != null) return group;
         }
         catch (Exception ex)
         {
            Log.Warning("Failed to find group {AccountName} in context {Context}: {Message}",
                        account.FullName, principalContext.ConnectedServer ?? "Local", ex.Message);
         }
      }

      return null;
   }

   public List<AccountInfo> GetAccountInfos(SecurityIdentifier sid)
   {
      ArgumentNullException.ThrowIfNull(sid);

      if (AccountCache.TryGetValue(sid, out var cachedAccountInfo))
         return cachedAccountInfo.ToList();

      var accountInfo = ResolveAllAccountInfos(sid);
      AccountCache.TryAdd(sid, accountInfo);
      return accountInfo.ToList();
   }

   private ConcurrentBag<AccountInfo> ResolveAllAccountInfos(SecurityIdentifier sid)
   {
      if (UnixIdentity.TryDescribe(sid, out var unixAccount)) return [unixAccount];

      var answers = new List<(DomainContext Source, AccountInfo Info)>();
      foreach (var domain in Serving(sid))
         if (domain is not DomainContext.Local || !DomainRouting.DirectoryAnswered(answers))
            answers.AddRange(Answers(sid, domain).Select(info => (domain, info)));
      var merged = DomainRouting.Merge(sid, answers);
      return merged.Count == 0 ? [new AccountInfo(sid)] : [..merged];
   }

   private List<DomainContext> Serving(SecurityIdentifier sid) =>
            Domains.Where(d => DomainRouting.Serves(d, sid, DomainSids)).ToList();

   private IReadOnlyList<AccountInfo> Answers(SecurityIdentifier sid, DomainContext domain)
   {
      var found = FromDirectory(sid, domain);
      return found.Count > 0
               ? found
               : domain switch
                 {
                          DomainContext.Local         => [DomainLookup.Lookup(sid)],
                          DomainContext.Server server => [DomainLookup.Lookup(sid, server.Name)],
                          _                           => []
                 };
   }

   private IReadOnlyList<AccountInfo> FromDirectory(SecurityIdentifier sid, DomainContext domain)
   {
      if (DomainRouting.Directory(domain) is not { } context || Unreadable.ContainsKey(domain)) return [];
      try
      {
         using var principal = Principal.FindByIdentity(context, IdentityType.Sid, sid.ToString());
         return principal is null ? [] : [CreateAccountInfo(principal)];
      }
      catch (Exception ex) when (ex is PrincipalServerDownException or UnauthorizedAccessException)
      {
         if (Unreadable.TryAdd(domain, true))
            Log.Error("{Context} cannot be read, its accounts stay limited or unresolved: {Message}",
                      DomainRouting.Name(domain), ex.Message);
      }
      catch (Exception ex)
      {
         Log.Warning("Failed to resolve SID {SecurityIdentifier} in {Context}: {Message}", sid,
                     DomainRouting.Name(domain), ex.Message);
      }

      return [];
   }

   private AccountInfo CreateAccountInfo(Principal principal)
   {
      var accountType  = DetermineAccountType(principal);
      var domain       = GetDomainFromPrincipal(principal);
      var accountState = DetermineAccountState(principal);

      return new AccountInfo(principal.Sid, domain, principal.Name, accountType)
             {
                      State = accountState
             };
   }

   private static AccountType DetermineAccountType(Principal principal)
   {
      if (string.IsNullOrEmpty(principal.Name)) return AccountType.UnIdentified;
      return principal is GroupPrincipal ? AccountType.Group : AccountType.User;
   }

   private string? GetDomainFromPrincipal(Principal principal)
   {
      if (string.IsNullOrEmpty(principal.Name) || string.IsNullOrEmpty(principal.DisplayName))
      {
         NetBiosName.TryGetValue(principal.Context, out var netbiosName);
         return netbiosName ?? principal.Context.ConnectedServer;
      }

      var domainArray = principal.DisplayName.Split("\\");
      if (domainArray.Length > 1) return domainArray[0];

      NetBiosName.TryGetValue(principal.Context, out var fallbackName);
      return fallbackName ?? principal.Context.ConnectedServer;
   }

   private static AccountState DetermineAccountState(Principal principal)
   {
      switch (principal)
      {
         case UserPrincipal userPrincipal:
            var result = (DirectoryEntry)userPrincipal.GetUnderlyingObject();
            var uacValue = result.Properties["useraccountcontrol"].Value;
            if (uacValue == null)
               return userPrincipal.Enabled == false ? AccountState.Disabled : AccountState.Enabled;
            return Convert.ToBoolean((int)uacValue & 0x2) ? AccountState.Disabled : AccountState.Enabled;
         case GroupPrincipal when !string.IsNullOrEmpty(principal.Name):
            return AccountState.Enabled;
         default:
            return AccountState.Unknown;
      }
   }

   internal static List<FileSystemAccessRule> AppliedRules(AuthorizationRuleCollection rules) =>
            rules.Cast<FileSystemAccessRule>().Where(rule => !rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)).ToList();

   private static ImmutableDictionary<SecurityIdentifier, FileSystemRights> ProcessAccessRules(
            IEnumerable<FileSystemAccessRule> accessRules, AccessControlType type)
   {
      return accessRules
                        .Where(x => x.AccessControlType == type)
                        .GroupBy(x => x.IdentityReference)
                        .ToImmutableDictionary(
                                               g => (SecurityIdentifier)g.Key,
                                               ProcessFileSystemRights);
   }

   private static FileSystemRights ProcessFileSystemRights(IGrouping<IdentityReference, FileSystemAccessRule> group)
   {
      var permissions = group.Aggregate((FileSystemRights)0,
                                        (current, rule) => current | rule.FileSystemRights);

      permissions |= MapGenericRightsToFileSystemRights(permissions);

      permissions = CleanPermissionFlags(permissions);

      if ((permissions & FileSystemRights.FullControl) == FileSystemRights.FullControl)
         permissions = FileSystemRights.FullControl;

      return permissions;
   }

   private static FileSystemRights CleanPermissionFlags(FileSystemRights permissions)
   {
      var permissionValue = (int)permissions;
      permissionValue <<= 8;
      permissionValue >>= 8;
      return (FileSystemRights)permissionValue;
   }

   private static string GetDomainNetBiosName(PrincipalContext principalContext)
   {
      if (!string.IsNullOrEmpty(principalContext.ConnectedServer))
      {
         var netBiosName = TryGetNetBiosFromDirectory(principalContext);
         return !string.IsNullOrEmpty(netBiosName)
                  ? netBiosName
                  : ExtractServerName(principalContext.ConnectedServer);
      }

      Log.Warning("Missing domain path, using machine name as fallback");
      return Environment.MachineName;
   }

   private static string? TryGetNetBiosFromDirectory(PrincipalContext principalContext)
   {
      try
      {
         var searchTask = Task.Run(() =>
         {
            using var searcher = new PrincipalSearcher(new UserPrincipal(principalContext));
            return searcher.FindOne();
         });

         if (!searchTask.Wait(TimeSpan.FromSeconds(3))) return null;

         using var result = searchTask.Result;
         if (result?.GetUnderlyingObject() is DirectoryEntry directoryEntry)
         {
            var dnPropertyString = directoryEntry.Path.Split("DC=")[1].Replace(",", string.Empty);
            if (!string.IsNullOrEmpty(dnPropertyString)) return dnPropertyString.ToUpper();
         }
      }
      catch (Exception ex)
      {
         Log.Debug("Failed to get NetBIOS name from directory: {Message}", ex.Message);
      }

      return null;
   }

   private static string ExtractServerName(string connectedServer)
   {
      var isIp = IPAddress.TryParse(connectedServer, out _);
      return connectedServer.Contains('.') && !isIp
               ? connectedServer.Split('.')[0].ToUpper()
               : connectedServer.ToUpper();
   }

   private static bool CleanAccountName(string name, out string? namePart, out string? domainPart)
   {
      var parts = name.Split('\\');
      if (parts.Length == 2)
      {
         namePart   = parts[1];
         domainPart = parts[0];
         return true;
      }

      namePart   = null;
      domainPart = null;
      return false;
   }

   private static string GetErrorType(Exception ex)
   {
      return ex switch
             {
                      UnauthorizedAccessException =>
                               $"Access denied: Insufficient permissions to check ACLs. {ex.Message}",
                      PathTooLongException => $"Path too long: The file path exceeds system limits. {ex.Message}",
                      DirectoryNotFoundException =>
                               $"Directory not found: The specified directory does not exist. {ex.Message}",
                      FileNotFoundException => $"File not found: The specified file does not exist. {ex.Message}",
                      IOException           => $"I/O Error: {ex.Message}",
                      SecurityException     => $"Security error: {ex.Message}",
                      _                     => $"Unexpected error ({ex.GetType().Name}): {ex.Message}"
             };
   }

   private static IdentityReference? GetOwnerIdentity(FileSystemSecurity fss, Type? type = null)
   {
      type ??= typeof(SecurityIdentifier);
      return fss switch
             {
                      DirectorySecurity ds => ds.GetOwner(type),
                      FileSecurity fs      => fs.GetOwner(type),
                      _                    => throw new InvalidOperationException("Invalid Security Type")
             };
   }

   private static AuthorizationRuleCollection FileSystemGetAccessControl(
            FileSystemSecurity fss, bool includeInherited, Type type)
   {
      return fss switch
             {
                      DirectorySecurity ds => ds.GetAccessRules(true, includeInherited, type),
                      FileSecurity fs      => fs.GetAccessRules(true, includeInherited, type),
                      _                    => throw new InvalidOperationException("Invalid Security Type")
             };
   }

   private static FileSystemRights MapGenericRightsToFileSystemRights(FileSystemRights originalRights)
   {
      var mappedRights = (FileSystemRights)0;
      var rightsValue  = (int)originalRights;

      if ((rightsValue & (int)GenericRights.GenericExecute) == (int)GenericRights.GenericExecute)
         mappedRights |= (FileSystemRights)MappedGenericRights.FileGenericExecute;

      if ((rightsValue & (int)GenericRights.GenericRead) == (int)GenericRights.GenericRead)
         mappedRights |= (FileSystemRights)MappedGenericRights.FileGenericRead;

      if ((rightsValue & (int)GenericRights.GenericWrite) == (int)GenericRights.GenericWrite)
         mappedRights |= (FileSystemRights)MappedGenericRights.FileGenericWrite;

      if ((rightsValue & (int)GenericRights.GenericAll) == (int)GenericRights.GenericAll ||
          (rightsValue & ((int)GenericRights.StandardRightsAll | (int)GenericRights.SpecificRightsAll)) ==
          ((int)GenericRights.StandardRightsAll | (int)GenericRights.SpecificRightsAll))
         return FileSystemRights.FullControl;

      return mappedRights;
   }
}