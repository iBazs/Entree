// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Buffers;
using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using Entree.MVVM.Model;

namespace Entree.Core.Handler;

internal static class ExportData
{
   public const string RawDataCsv = "RawData.csv";

   public static readonly SecurityIdentifier NullSid = new(WellKnownSidType.NullSid, null);

   private static readonly SearchValues<char> FormulaStarts = SearchValues.Create("=+-@\t\r");

   public static readonly string[] IdentityHeaders =
            ["Full Name", "Name", "Domain", "Security Identifier(SID)", "Type", "Status", "Error"];

   public static IEnumerable<(SecurityIdentifier Identity, FileSystemRights Rights, AccessControlType? Type)> AceRows(
            FileSystemStruct entry) =>
            entry.AceCount == 0
                     ? [(NullSid, 0, null)]
                     : entry.Aces.Select(ace => (ace.Identity, ace.Rights, (AccessControlType?)ace.Type));

   public static string[] RawHeaders(bool showOwner) =>
            showOwner
                     ? ["Path", "Attributes", "Owner", "Identity", "Account Rights", "Access Type", "Last Modified Date", "Error"]
                     : ["Path", "Attributes", "Identity", "Account Rights", "Access Type", "Last Modified Date", "Error"];

   public static string? CsvCell(string? value) =>
            value is [var first, ..] && FormulaStarts.Contains(first) ? string.Concat("'", value) : value;

   public static string? FromCsvCell(string? value) =>
            value is ['\'', var next, ..] && FormulaStarts.Contains(next) ? value[1..] : value;

   public static List<SecurityIdentifier> MatrixSids(
            ConcurrentDictionary<SecurityIdentifier, List<AccountInfo>> accounts, IEnumerable<string> excluded)
   {
      var names = excluded.ToHashSet(StringComparer.OrdinalIgnoreCase);
      return accounts.Where(pair => !pair.Value.Any(a => names.Contains(a.FullName) ||
                                                         (a.Name != null && names.Contains(a.Name)) ||
                                                         names.Contains(a.Sid.Value)))
                     .OrderBy(pair => SortKey(pair.Value[0]))
                     .Select(pair => pair.Key)
                     .ToList();
   }

   private static (int, string, string, string) SortKey(AccountInfo account) =>
            (!string.IsNullOrEmpty(account.Domain) && !string.IsNullOrEmpty(account.Name) ? 0
             : !string.IsNullOrEmpty(account.Name)                                        ? 1 : 2,
             account.Domain ?? string.Empty, account.Name ?? string.Empty, account.Sid.Value);
}
