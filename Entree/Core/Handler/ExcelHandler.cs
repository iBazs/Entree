// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using LargeXlsx;
using Serilog;
using Entree.Core.BaseClass;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;

namespace Entree.Core.Handler;

public class ExcelHandler(
         SearchConfigViewModel searchConfig,
         IReadOnlyList<FileSystemStruct> resultList,
         List<AccountInfo[]> groupsList,
         ConcurrentDictionary<SecurityIdentifier, List<AccountInfo>> accountsDictionary)
{
   private static readonly XlsxStyle DateStyle = XlsxStyle.Default.With(XlsxNumberFormat.ShortDateTime);

   private          CancellationToken                             _cToken;
   private          HashSet<SecurityIdentifier>                   _duplicateSids = [];

   public void Export(string outputPath, CancellationToken cancellationToken)
   {
      _cToken = cancellationToken;
      cancellationToken.ThrowIfCancellationRequested();
      _duplicateSids = accountsDictionary.Where(pair => pair.Value.Count > 1).Select(pair => pair.Key).ToHashSet();

      using (var fileStream = File.Create(outputPath))
      using (var excel = new XlsxWriter(fileStream, XlsxCompressionLevel.Optimal, requireCellReferences: false))
      {
         RawDataSheet(excel);
         if (searchConfig.ShowMatrix) MatrixSheet(excel);
         if (searchConfig.ShowGroups) GroupsSheet(excel);
         IdentitySheet(excel);
      }

      Log.Information("Excel export completed successfully to {Path}", outputPath);
   }

   private void RawDataSheet(XlsxWriter excel)
   {
      var headers = ExportData.RawHeaders(searchConfig.ShowOwner);
      int[] widths = searchConfig.ShowOwner ? [66, 21, 54, 54, 21, 12, 18, 12] : [66, 21, 54, 21, 12, 18, 12];

      var globalEntryIndex = 0;

      for (var pageNumber = 0; pageNumber == 0 || globalEntryIndex < resultList.Count; pageNumber++)
      {
         var sheetName = pageNumber == 0
                  ? "RawData"
                  : new StringBuilder().Append("RawData_").Append(pageNumber + 1).ToString();
         Log.Information("Creating sheet: {SheetName}", sheetName);

         var columns = widths.Select(columnWidth => XlsxColumn.Formatted(columnWidth)).ToList();
         var worksheet = excel.BeginWorksheet(sheetName, 1, columns: columns)
                              .SetAutoFilter(1, 1, 1, headers.Length);

         worksheet.BeginRow();
         foreach (var header in headers) worksheet.Write(header, ReportHelper.HeaderStyle);

         var rowsOnCurrentPage = 0;

         while (globalEntryIndex < resultList.Count && rowsOnCurrentPage < ReportHelper.MaxRows)
         {
            _cToken.ThrowIfCancellationRequested();

            var entry = resultList[globalEntryIndex];

            if (rowsOnCurrentPage > 0 && rowsOnCurrentPage + Math.Max(1, entry.AceCount) > ReportHelper.MaxRows) break;
            foreach ((var identity, var rights, var type) in ExportData.AceRows(entry))
            {
               WriteRow(worksheet, entry, identity, rights, type);
               rowsOnCurrentPage++;
            }

            globalEntryIndex++;
         }
      }
   }

   private void WriteRow(XlsxWriter sheet, FileSystemStruct entry, SecurityIdentifier identity, FileSystemRights rights,
                         AccessControlType? accessType)
   {
      sheet.BeginRow();
      sheet.Write(entry.Path);
      sheet.Write(entry.FileEntryAttributes.ToString());
      if (searchConfig.ShowOwner) sheet.Write(entry.Owner?.Value);

      if (identity.Equals(ExportData.NullSid)) sheet.Write();
      else sheet.Write(identity.ToString());

      if (rights == 0) sheet.Write();
      else sheet.Write(rights.ToString());

      sheet.Write(accessType?.ToString());

      if (entry.LastWriteTime == DateTime.MinValue) sheet.Write();
      else sheet.Write(entry.LastWriteTime, DateStyle);
      sheet.Write(entry.Error);
   }

   private void MatrixSheet(XlsxWriter excel)
   {
      var aclSids    = ExportData.MatrixSids(accountsDictionary, searchConfig.ExcludedAccounts);
      var partSize   = (int)ReportHelper.MaxMatrixIdentityPart;
      var totalParts = (aclSids.Count + partSize - 1) / partSize;

      if (aclSids.Count > partSize)
         Log.Warning("Warning: Currently found users count is {IdentityCount}. Will be separated into {TotalParts} parts per page.",
                     aclSids.Count, totalParts);

      var page = 0;
      foreach (var pageRows in AclMatrix.Create(resultList, aclSids).Chunk(ReportHelper.MaxRows))
      {
         for (var part = 0; part < totalParts; part++)
         {
            var partSuffix = totalParts == 1 ? "" : $"-{part + 1}";
            var sheetName  = "Matrix" + (page == 0 && totalParts == 1 ? "" : $"_{page + 1}{partSuffix}");
            Log.Information("Creating sheet: {SheetName}", sheetName);
            MatrixCreation(excel, sheetName, pageRows, aclSids.Skip(part * partSize).Take(partSize).ToList(),
                           part * partSize);
         }

         page++;
      }
   }

   private void MatrixCreation(XlsxWriter xlsxWriter, string sheetName, IEnumerable<ReportHelper.MatrixRow> rows,
                               List<SecurityIdentifier> partSidsList, int firstColumn)
   {
      if (accountsDictionary is not { IsEmpty: false }) return;
      var forestGreen = Color.FromArgb(33, 150, 70);

      var pathStyle = new XlsxStyle(XlsxStyle.Default.Font.With(forestGreen).WithBold(),
                                    XlsxStyle.Default.Fill,
                                    XlsxStyle.Default.Border,
                                    XlsxStyle.Default.NumberFormat,
                                    XlsxStyle.Default.Alignment);

      var enabledHeaderStyle =
               ReportHelper.HeaderStyle.With(new XlsxAlignment(XlsxAlignment.Horizontal.Center,
                                                               textRotation: 90));
      var unknownHeaderStyle = ReportHelper.UnknownHeaderStyle.With(new XlsxAlignment(XlsxAlignment.Horizontal.Center,
                                                                           textRotation: 90));

      var disabledHeaderStyle = ReportHelper.DisabledStyle.With(new XlsxAlignment(XlsxAlignment.Horizontal.Center,
                                                                       textRotation: 90));
      var duplicateHeaderStyle =
               ReportHelper.DuplicateStyle.With(new XlsxAlignment(XlsxAlignment.Horizontal.Center,
                                                                  textRotation: 90));
      var limitedHeaderStyle =
               ReportHelper.LimitedStyle.With(new XlsxAlignment(XlsxAlignment.Horizontal.Center, textRotation: 90));

      List<XlsxColumn> columns = [XlsxColumn.Formatted(60), ..partSidsList.Select(_ => XlsxColumn.Formatted(3))];

      var worksheet = xlsxWriter.BeginWorksheet(sheetName,
                                                columns: columns, splitRow: 1, splitColumn: 1)
                                .SetAutoFilter(1, 1, 1, partSidsList.Count + 1);

      worksheet.BeginRow();
      worksheet.Write("Path", pathStyle);

      foreach (var sid in partSidsList)
      {
         _cToken.ThrowIfCancellationRequested();
         var accountInfo = accountsDictionary[sid].First();
         worksheet.Write(accountInfo.FullName, accountInfo.State switch
                                               {
                                                        AccountState.Disabled             => disabledHeaderStyle,
                                                        AccountState.Unknown              => unknownHeaderStyle,
                                                        _ when _duplicateSids.Contains(sid) => duplicateHeaderStyle,
                                                        AccountState.Limited              => limitedHeaderStyle,
                                                        _                                 => enabledHeaderStyle
                                               });
      }

      foreach (var matrixRow in rows)
      {
         _cToken.ThrowIfCancellationRequested();
         worksheet.BeginRow();
         worksheet.Write(matrixRow.Path);

         for (var column = firstColumn; column < firstColumn + partSidsList.Count; column++)
         {
            var accessType = matrixRow.Access[column];
            (var character, var style) = ReportHelper.GetAccessStyle(accessType);
            if (string.IsNullOrEmpty(character)) worksheet.Write();
            else worksheet.WriteSharedString(character, style);
         }
      }
   }

   private void GroupsSheet(XlsxWriter xlsxWriter)
   {
      Log.Information("Creating sheet: Groups");
      if (groupsList is not { Count: not 0 })
      {
         Log.Error("No groups found.");
         return;
      }

      var parts = groupsList.Chunk(ReportHelper.MaxSheetColumns).ToList();
      for (var part = 0; part < parts.Count; part++)
         GroupsPart(xlsxWriter, parts.Count == 1 ? "Groups" : $"Groups_{part + 1}", parts[part]);
   }

   private void GroupsPart(XlsxWriter xlsxWriter, string sheetName, AccountInfo[][] groups)
   {
      xlsxWriter.BeginWorksheet(sheetName, 1).SetAutoFilter(1, 1, 1, groups.Length);
      xlsxWriter.BeginRow(30);

      foreach (var group in groups)
      {
         _cToken.ThrowIfCancellationRequested();
         xlsxWriter.Write(group[^1].FullName, ReportHelper.GroupHeaderStyle);
      }

      var maxGroupSize = groups.Max(group => group.Length - 1);

      for (var position = 0; position < maxGroupSize; position++)
      {
         _cToken.ThrowIfCancellationRequested();
         xlsxWriter.BeginRow();

         var currentPosition = position;
         foreach (var currentAccount in from @group in groups
                                        where currentPosition < @group.Length - 1
                                        select @group[currentPosition])
         {
            xlsxWriter.Write(currentAccount.FullName,
                             ReportHelper.AccountStyle(currentAccount.State, _duplicateSids.Contains(currentAccount.Sid)));
         }
      }
   }

   private void IdentitySheet(XlsxWriter xlsxWriter)
   {
      if (accountsDictionary is not { Count: not 0 })
      {
         Log.Error("No identities found.");
         return;
      }

      Log.Information("Creating sheet: Identity");
      var headers = ExportData.IdentityHeaders;
      var columns = headers.Select((_, i) => XlsxColumn.Formatted(i < 3 ? 30 : 9)).ToList();
      xlsxWriter.BeginWorksheet("Identity", 1, columns: columns).SetAutoFilter(1, 1, 1, headers.Length);
      xlsxWriter.BeginRow(30);

      foreach (var header in headers) xlsxWriter.Write(header, ReportHelper.HeaderStyle);
      foreach (var account in accountsDictionary.SelectMany(x => x.Value).OrderBy(x => x.FullName))
      {
         _cToken.ThrowIfCancellationRequested();
         xlsxWriter.BeginRow();
         var style = ReportHelper.AccountStyle(account.State, _duplicateSids.Contains(account.Sid));
         xlsxWriter.Write(account.FullName, style);
         xlsxWriter.Write(account.Name, style);
         xlsxWriter.Write(account.Domain, style);
         xlsxWriter.Write(account.Sid.Value, style);
         xlsxWriter.Write(account.Type.ToString(), style);
         xlsxWriter.Write(account.State.ToString(), style);
         xlsxWriter.Write(account.Error, style);
      }
   }
}