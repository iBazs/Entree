// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Entree.MVVM.Model;

public sealed record SecurityInfo(
         SecurityIdentifier? Owner,
         ImmutableDictionary<SecurityIdentifier, FileSystemRights> Allow,
         ImmutableDictionary<SecurityIdentifier, FileSystemRights> Deny,
         string? Error)
{
   private const int MaxInternedFailures = 1024;

   private static readonly ConcurrentDictionary<string, SecurityInfo> Failures = new(StringComparer.Ordinal);

   public static SecurityInfo Empty { get; } = new(null, ImmutableDictionary<SecurityIdentifier, FileSystemRights>.Empty,
                                                   ImmutableDictionary<SecurityIdentifier, FileSystemRights>.Empty, null);

   public static SecurityInfo Failed(string error) =>
            Failures.TryGetValue(error, out var known) ? known
            : Failures.Count < MaxInternedFailures   ? Failures.GetOrAdd(error, e => Empty with { Error = e })
                                                       : Empty with { Error = error };
}

[StructLayout(LayoutKind.Auto)]
public readonly struct FileSystemStruct
{
   private readonly SecurityInfo? _security;

   public string         Path                { get; init; }
   public FileAttributes FileEntryAttributes { get; init; }
   public DateTime       LastWriteTime       { get; init; }

   public SecurityInfo Security
   {
      get => _security ?? SecurityInfo.Empty;
      init => _security = value;
   }

   public SecurityIdentifier?                                       Owner             => Security.Owner;
   public ImmutableDictionary<SecurityIdentifier, FileSystemRights> AccessControlList => Security.Allow;
   public ImmutableDictionary<SecurityIdentifier, FileSystemRights> DenyControlList   => Security.Deny;
   public string?                                                   Error             => Security.Error;
   public bool                                                      HasError          => !string.IsNullOrWhiteSpace(Error);
   public int                                                       AceCount          => AccessControlList.Count + DenyControlList.Count;

   public IEnumerable<(SecurityIdentifier Identity, FileSystemRights Rights, AccessControlType Type)> Aces =>
            AccessControlList.Select(a => (a.Key, a.Value, AccessControlType.Allow))
                             .Concat(DenyControlList.Select(a => (a.Key, a.Value, AccessControlType.Deny)));

   public override string ToString() =>
            $"Path: {Path}, {(Owner != null ? "Owner : " + Owner + ", " : "")}ACL Count: {AccessControlList.Count}, Last Modified Date: {LastWriteTime}, Error: {Error ?? "-"}";
}

[StructLayout(LayoutKind.Auto)]
public readonly struct FlatFileSystemStruct
{
   public string              Path           { get; init; }
   public SecurityIdentifier? Owner          { get; init; }
   public FileAttributes?     FileAttributes { get; init; }
   public SecurityIdentifier? Identity       { get; init; }
   public FileSystemRights?   Access         { get; init; }
   public AccessControlType   AccessType     { get; init; }
   public DateTime            LastWriteTime  { get; init; }
}

public class ComparedFileSystemStructResult
{
   public string              Path                   { get; init; } = "Path";
   public SecurityIdentifier? Owner                  { get; set; }
   public SecurityIdentifier? NewOwner               { get; set; }
   public FileAttributes?     FileEntryAttributes    { get; set; }
   public FileAttributes?     NewFileEntryAttributes { get; set; }
   public SecurityIdentifier? Identity               { get; set; }
   public FileSystemRights?   Access                 { get; set; }
   public FileSystemRights?   NewAccess              { get; set; }
   public AccessControlType   AccessType             { get; init; }
   public DateTime            LastWriteTime          { get; set; }
   public DateTime            NewLastWriteTime       { get; set; }
   public string?             Difference             { get; set; }

   public DifferenceKind Kind { get; set; }
}

[Flags]
public enum DifferenceKind
{
   None       = 0,
   Owner      = 1,
   Attributes = 2,
   Access     = 4,
   Date       = 8
}