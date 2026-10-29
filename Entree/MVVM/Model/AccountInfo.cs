// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Security.Principal;
using Entree.Core.BaseClass;

namespace Entree.MVVM.Model;

public class AccountInfo(
         SecurityIdentifier sid,
         string? domain = null,
         string? name = null,
         AccountType type = AccountType.UnIdentified,
         AccountState accountState = AccountState.Unknown,
         string? error = null) : IEquatable<AccountInfo>
{
   public string?            Domain { get; set; }  = domain;
   public string?            Name   { get; set; }  = name;
   public AccountType        Type   { get; }       = type;
   public SecurityIdentifier Sid    { get; }       = sid;
   public AccountState       State  { get; set; }  = accountState;
   public string?            Error  { get; }       = error;

   public bool IsEnabled    => State == AccountState.Enabled;

   public string FullName => Name != null
            ? Path.Combine(Domain ?? "", Name)
            : Sid.Value;

   public bool Equals(AccountInfo? other) => other is not null && (ReferenceEquals(this, other) || Sid.Equals(other.Sid));

   public override string ToString() => $"Identity: {Name} \n"
                                        + $"Domain: {Domain} \n"
                                        + $"State: {State} \n"
                                        + $"Type: {Type} \n"
                                        + $"Original SID: {Sid}\n";

   public override bool Equals(object? obj) => Equals(obj as AccountInfo);

   public static bool operator ==(AccountInfo? item1, AccountInfo? item2) => item1?.Equals(item2) ?? item2 is null;

   public static bool operator !=(AccountInfo? item1, AccountInfo? item2) => !(item1 == item2);

   public override int GetHashCode() => Sid.GetHashCode();
}