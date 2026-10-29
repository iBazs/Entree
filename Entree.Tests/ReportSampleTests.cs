// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Entree.Core.BaseClass;
using Entree.Core.Handler;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;
using Xunit;

namespace Entree.Tests;

public class ReportSampleTests
{
   private static readonly string ScreenDirectory = Path.Combine(AppContext.BaseDirectory, "screens");

   private static readonly AccountInfo Editors  = Group("S-1-5-21-10-20-30-1101", "CORP", "Finance Editors", AccountState.Enabled);
   private static readonly AccountInfo Readers  = Group("S-1-5-21-10-20-30-1102", "CORP", "Finance Readers", AccountState.Enabled);
   private static readonly AccountInfo Alice    = User("S-1-5-21-10-20-30-1201", "CORP", "alice", AccountState.Enabled);
   private static readonly AccountInfo Bob      = User("S-1-5-21-10-20-30-1202", "CORP", "bob", AccountState.Disabled);
   private static readonly AccountInfo Partner  = Group("S-1-5-21-40-50-60-1301", "PARTNER", "Partner Staff", AccountState.Limited);
   private static readonly AccountInfo Admins   = Group("S-1-5-32-544", "BUILTIN", "Administrators", AccountState.Enabled);
   private static readonly AccountInfo Server   = User("S-1-5-21-70-80-90-500", "FILESERVER", "Administrator", AccountState.Enabled);
   private static readonly AccountInfo Clone    = User("S-1-5-21-70-80-90-500", "PC01", "Administrator", AccountState.Enabled);
   private static readonly AccountInfo Deleted  = new(new SecurityIdentifier("S-1-5-21-10-20-30-4242"), error: "No mapping between account names and security IDs was done.");

   [Fact]
   public void SampleReport_ListsEveryAccountState()
   {
      Directory.CreateDirectory(ScreenDirectory);
      var path = Path.Combine(ScreenDirectory, "Report.xlsx");
      var config = new SearchConfigViewModel(@"\\fileserver\finance", ScreenDirectory, SearchFor.Folders, openResult: false);

      new ExcelHandler(config, Entries(), Groups(), Accounts()).Export(path, CancellationToken.None);

      var workbook = XlsxSheetReader.Load(path);
      var statuses = XlsxSheetReader.ReadRows(workbook, "Identity").Skip(1).Select(row => row[5]).ToHashSet();
      Assert.Equal(["RawData", "Matrix", "Groups", "Identity"], workbook.Sheets.Select(s => s.Name));
      Assert.Superset(new HashSet<string?> { "Enabled", "Disabled", "Limited", "Unknown" }, statuses);
   }

   private static List<FileSystemStruct> Entries() =>
   [
            Entry(@"\\fileserver\finance", (Admins, FileSystemRights.FullControl), (Editors, FileSystemRights.Modify),
                  (Readers, FileSystemRights.ReadAndExecute)),
            Entry(@"\\fileserver\finance\Budget", (Admins, FileSystemRights.FullControl), (Editors, FileSystemRights.Modify),
                  (Readers, FileSystemRights.ReadAndExecute)),
            Entry(@"\\fileserver\finance\Payroll", (Admins, FileSystemRights.FullControl), (Alice, FileSystemRights.Modify),
                  (Server, FileSystemRights.FullControl)),
            Entry(@"\\fileserver\finance\Partner", (Admins, FileSystemRights.FullControl), (Partner, FileSystemRights.ReadAndExecute),
                  (Bob, FileSystemRights.Write)),
            Entry(@"\\fileserver\finance\Archive", (Admins, FileSystemRights.FullControl), (Readers, FileSystemRights.ListDirectory),
                  (Deleted, FileSystemRights.Read)),
            new FileSystemStruct
            {
                     Path                = @"\\fileserver\finance\Audit",
                     FileEntryAttributes = FileAttributes.Directory,
                     LastWriteTime       = new DateTime(2026, 10, 1, 9, 0, 0),
                     Security            = SecurityInfo.Failed("Access denied: Insufficient permissions to check ACLs.")
            }
   ];

   private static FileSystemStruct Entry(string path, params (AccountInfo Account, FileSystemRights Rights)[] aces) =>
            new()
            {
                     Path                = path,
                     FileEntryAttributes = FileAttributes.Directory,
                     LastWriteTime       = new DateTime(2026, 10, 27, 14, 30, 0),
                     Security = SecurityInfo.Empty with
                     {
                              Owner = Admins.Sid,
                              Allow = aces.ToImmutableDictionary(a => a.Account.Sid, a => a.Rights)
                     }
            };

   private static List<AccountInfo[]> Groups() =>
   [
            [Alice, Readers, Partner, Editors],
            [Alice, Bob, Readers],
            [Server, Clone, Admins]
   ];

   private static ConcurrentDictionary<SecurityIdentifier, List<AccountInfo>> Accounts() =>
            new(new[] { Editors, Readers, Alice, Bob, Partner, Admins, Deleted }
                        .Select(a => new KeyValuePair<SecurityIdentifier, List<AccountInfo>>(a.Sid, [a]))
                        .Append(new(Server.Sid, [Server, Clone])));

   private static AccountInfo Group(string sid, string domain, string name, AccountState state) =>
            new(new SecurityIdentifier(sid), domain, name, AccountType.Group, state);

   private static AccountInfo User(string sid, string domain, string name, AccountState state) =>
            new(new SecurityIdentifier(sid), domain, name, AccountType.User, state);
}
