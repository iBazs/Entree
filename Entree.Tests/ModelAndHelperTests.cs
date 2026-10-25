// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Serilog.Events;
using Serilog.Parsing;
using Wpf.Ui.Appearance;
using Entree.Core.BaseClass;
using Entree.Core.Handler;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.MVVM.View.Converter;
using Entree.MVVM.ViewModel.Control;
using Xunit;

namespace Entree.Tests;

public class ModelAndHelperTests
{
   private static readonly SecurityIdentifier SidA = new("S-1-5-21-1-2-3-1001");
   private static readonly SecurityIdentifier SidB = new("S-1-5-21-1-2-3-1002");

   [Fact]
   public void MatrixSids_ExcludesAccountsIgnoringCase()
   {
      var accounts = new System.Collections.Concurrent.ConcurrentDictionary<SecurityIdentifier, List<AccountInfo>>
                     {
                              [SidA] = [new AccountInfo(SidA, "CORP", "Alice")],
                              [SidB] = [new AccountInfo(SidB, "CORP", "Bob")]
                     };

      Assert.Equal([SidB], ExportData.MatrixSids(accounts, ["corp\\alice"]));
      Assert.Equal([SidA], ExportData.MatrixSids(accounts, ["BOB"]));
      Assert.Empty(ExportData.MatrixSids(accounts, [SidA.Value, SidB.Value]));
   }

   [Fact]
   public void AccountInfo_UnresolvedSid_EqualsItselfButNotAnotherSid()
   {
      var a = new AccountInfo(SidA);

      Assert.True(a.Equals(new AccountInfo(SidA)));
      Assert.True(a == new AccountInfo(SidA));
      Assert.False(a == new AccountInfo(SidB));
      Assert.Equal(a.GetHashCode(), new AccountInfo(SidA, type: AccountType.User).GetHashCode());
   }

   [Fact]
   public void AccountInfo_SameNameDifferentSid_AreNotEqual()
   {
      Assert.False(new AccountInfo(SidA, "DOM1", "jsmith") == new AccountInfo(SidB, "DOM2", "jsmith"));
   }

   [Fact]
   public void AccountInfo_NullOperands()
   {
      AccountInfo? none = null;
      Assert.True(none == null);
      Assert.False(new AccountInfo(SidA) == none);
   }

   [Theory]
   [InlineData(4, 4)]
   [InlineData(512, 512)]
   public void Parallelism_KeepsValidLimit(int limit, int expected) =>
      Assert.Equal(expected, Parallelism.Degree(limit));

   [Theory]
   [InlineData(0)]
   [InlineData(-1)]
   [InlineData(-5)]
   [InlineData(10_000)]
   public void Parallelism_InvalidLimitFallsBackToProcessorCount(int limit) =>
      Assert.Equal(Math.Min(Environment.ProcessorCount, 512), Parallelism.Degree(limit));

   [Theory]
   [InlineData(FileSystemRights.FullControl, AccessType.FullControl)]
   [InlineData(FileSystemRights.Modify | FileSystemRights.Synchronize, AccessType.Modify)]
   [InlineData(FileSystemRights.Write | FileSystemRights.Synchronize, AccessType.Write)]
   [InlineData(FileSystemRights.Read | FileSystemRights.Synchronize, AccessType.Read)]
   [InlineData(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize, AccessType.Read)]
   [InlineData(FileSystemRights.ListDirectory, AccessType.List)]
   [InlineData(FileSystemRights.Delete, AccessType.Special)]
   public void GetAccessType_ClassifiesByContainment(FileSystemRights rights, AccessType expected) =>
      Assert.Equal(expected, ReportHelper.GetAccessType(rights));

   [Fact]
   public void FileSystemStruct_DefaultAccessControlList_IsEmpty() =>
      Assert.Empty(new FileSystemStruct { Path = @"C:\x", Security = SecurityInfo.Failed("denied") }.AccessControlList);


   [Fact]
   public void FileSystemStruct_Aces_ListsAllowThenDeny()
   {
      var entry = new FileSystemStruct
                  {
                           Path = @"C:\x",
                           Security = SecurityInfo.Empty with
                                      {
                                               Allow = SecurityInfo.Empty.Allow.Add(SidA, FileSystemRights.Read),
                                               Deny = SecurityInfo.Empty.Deny.Add(SidA, FileSystemRights.Write)
                                      }
                  };

      Assert.Equal(2, entry.AceCount);
      Assert.Equal([(SidA, FileSystemRights.Read, AccessControlType.Allow),
                    (SidA, FileSystemRights.Write, AccessControlType.Deny)], entry.Aces);
   }

   [Theory]
   [InlineData("Deny", AccessControlType.Deny)]
   [InlineData("allow", AccessControlType.Allow)]
   [InlineData("", AccessControlType.Allow)]
   [InlineData(null, AccessControlType.Allow)]
   public void ParseAccessType_DefaultsToAllowForOldReports(string? value, AccessControlType expected) =>
      Assert.Equal(expected, Reader.ParseAccessType(value));

   [Theory]
   [InlineData("A1", 0)]
   [InlineData("Z9", 25)]
   [InlineData("AA10", 26)]
   [InlineData("", 4)]
   [InlineData(null, 4)]
   public void ColumnIndex_FromCellReference(string? reference, int expected) =>
      Assert.Equal(expected, XlsxSheetReader.ColumnIndex(reference, 4));

   [Fact]
   public void ParseDate_OaDateAndCsvTextAgreeToTheSecond()
   {
      var stamp = new DateTime(2026, 6, 28, 21, 44, 35, 678);
      var fromXlsx = Reader.ParseDate(stamp.ToOADate().ToString(CultureInfo.InvariantCulture));
      var fromCsv  = Reader.ParseDate(stamp.ToString("yyyy-MM-dd HH:mm:ss"));

      Assert.Equal(new DateTime(2026, 6, 28, 21, 44, 35), fromXlsx);
      Assert.Equal(fromXlsx, fromCsv);
      Assert.Equal(DateTime.MinValue, Reader.ParseDate(null));
   }

   [Theory]
   [InlineData(@"C:\_x0041_lit", @"C:\Alit")]
   [InlineData(@"C:\_x005F_x0041_lit", @"C:\_x0041_lit")]
   [InlineData(@"C:\plain", @"C:\plain")]
   public void Unescape_DecodesOoxmlEscapes(string raw, string expected) =>
      Assert.Equal(expected, XlsxSheetReader.Unescape(raw));

   [Theory]
   [InlineData(-693594, 1, 1, 1, 0)]
   [InlineData(-109206, 1601, 1, 1, 0)]
   [InlineData(-1, 1899, 12, 30, 0)]
   [InlineData(59, 1900, 2, 28, 0)]
   [InlineData(61, 1900, 3, 1, 0)]
   [InlineData(46317.916666666664, 2026, 10, 22, 22)]
   public void FromExcelSerial_MatchesLargeXlsxEncoding(double serial, int year, int month, int day, int hour) =>
      Assert.Equal(new DateTime(year, month, day, hour, 0, 0), Reader.FromExcelSerial(serial));

   [Theory]
   [InlineData(double.NaN)]
   [InlineData(-1e9)]
   [InlineData(1e9)]
   public void FromExcelSerial_OutOfRangeIsMinValue(double serial) =>
      Assert.Equal(DateTime.MinValue, Reader.FromExcelSerial(serial));

   [Theory]
   [InlineData("0.9.0+737ebe8a6a022b455a24bdbbae963c414c47fd2e", "0.9.0 (737ebe8)")]
   [InlineData("1.0.0-beta.1+abc", "1.0.0-beta.1 (abc)")]
   [InlineData("0.9.0", "0.9.0")]
   [InlineData(null, "Unknown")]
   public void FormatVersion_ShowsVersionAndShortCommit(string? informational, string expected) =>
      Assert.Equal(expected, SettingsViewModel.FormatVersion(informational));

   [Fact]
   public void LogLine_ShowsTimeLevelMessageAndException()
   {
      var logEvent = new LogEvent(new DateTimeOffset(2026, 10, 25, 9, 5, 7, TimeSpan.Zero),
                                  LogEventLevel.Warning, new IOException("disk"),
                                  new MessageTemplateParser().Parse("Scan {Path}"),
                                  [new LogEventProperty("Path", new ScalarValue("C:\\Data"))]);
      Assert.Equal("09:05:07  WRN  Scan \"C:\\Data\"  (disk)", LogEventRenderConverter.Format(logEvent));
   }

   [Theory]
   [InlineData("Light", SystemTheme.Dark, ApplicationTheme.Light)]
   [InlineData("Dark", SystemTheme.Light, ApplicationTheme.Dark)]
   [InlineData("System", SystemTheme.Light, ApplicationTheme.Light)]
   [InlineData("System", SystemTheme.Sunrise, ApplicationTheme.Light)]
   [InlineData("System", SystemTheme.Glow, ApplicationTheme.Dark)]
   [InlineData(null, SystemTheme.Unknown, ApplicationTheme.Dark)]
   public void AppTheme_ResolvesChoiceAgainstWindows(string? choice, SystemTheme system,
                                                     ApplicationTheme expected) =>
      Assert.Equal(expected, AppTheme.Resolve(choice, system));

   [Theory]
   [InlineData("=HYPERLINK(\"x\")", "'=HYPERLINK(\"x\")")]
   [InlineData("+cmd", "'+cmd")]
   [InlineData("-1610612736", "'-1610612736")]
   [InlineData("@SUM(A1)", "'@SUM(A1)")]
   [InlineData(@"C:\Data", @"C:\Data")]
   [InlineData("", "")]
   public void CsvCell_PrefixesFormulaStartsAndReadsBack(string value, string written)
   {
      Assert.Equal(written, ExportData.CsvCell(value));
      Assert.Equal(value, ExportData.FromCsvCell(written));
   }

   [Fact]
   public void FromCsvCell_KeepsAnApostropheThatGuardsNothing() =>
      Assert.Equal("'quoted", ExportData.FromCsvCell("'quoted"));
}
