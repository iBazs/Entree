// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using nietras.SeparatedValues;
using Serilog;
using Entree.Core.BaseClass;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;

namespace Entree.Core.Handler;

public class CsvHandler(
         SearchConfigViewModel searchConfig,
         IReadOnlyList<FileSystemStruct> resultList,
         List<AccountInfo[]> groupsList,
         ConcurrentDictionary<SecurityIdentifier, List<AccountInfo>> accountsDictionary)
{
   private const CompressionLevel EntryCompression = CompressionLevel.SmallestSize;

   private CancellationToken _cToken;

   public void Export(string outputPath, CancellationToken cancellationToken)
   {
      _cToken = cancellationToken;
      cancellationToken.ThrowIfCancellationRequested();
      using (var zipArchive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
      {
         AddEntry(zipArchive, ExportData.RawDataCsv, WriteRawDataCsvWithSep);
         if (searchConfig.ShowMatrix) AddEntry(zipArchive, "Matrix.csv", WriteMatrixCsvWithSep);
         if (searchConfig.ShowGroups) AddEntry(zipArchive, "Groups.csv", WriteGroupsCsvWithSep);
         AddEntry(zipArchive, "Identity.csv", WriteIdentityCsvWithSep);
      }

      Log.Information("CSV export completed successfully to {Path}", outputPath);
   }

   private void AddEntry(ZipArchive zipArchive, string name, Action<Stream> write)
   {
      _cToken.ThrowIfCancellationRequested();
      using var stream = zipArchive.CreateEntry(name, EntryCompression).Open();
      write(stream);
   }

   private SepWriter NewWriter(Stream stream, IReadOnlyList<string> headers)
   {
      var writer = Sep.Writer(o => o with { Escape = true, WriteHeader = false }).To(stream, true);
      WriteRow(writer, headers);
      return writer;
   }

   private void WriteRow(SepWriter writer, IReadOnlyList<string> values)
   {
      _cToken.ThrowIfCancellationRequested();
      using var row = writer.NewRow();
      for (var i = 0; i < values.Count; i++) row[i].Set(ExportData.CsvCell(values[i]));
   }

   private void WriteRawDataCsvWithSep(Stream stream)
   {
      using var writer = NewWriter(stream, ExportData.RawHeaders(searchConfig.ShowOwner));
      foreach (var entry in resultList)
      foreach (var (identity, rights, type) in ExportData.AceRows(entry))
         WriteRow(writer, RawLine(entry, identity, rights, type));
   }

   private string[] RawLine(FileSystemStruct entry, SecurityIdentifier identity, FileSystemRights rights,
                            AccessControlType? type) =>
   [
            entry.Path,
            entry.FileEntryAttributes.ToString(),
            .. searchConfig.ShowOwner ? (string[])[entry.Owner?.Value ?? string.Empty] : [],
            identity.Equals(ExportData.NullSid) ? string.Empty : identity.ToString(),
            rights == 0 ? string.Empty : rights.ToString(),
            type?.ToString() ?? string.Empty,
            entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            entry.Error ?? string.Empty
   ];

   private void WriteMatrixCsvWithSep(Stream stream)
   {
      var       aclSids = ExportData.MatrixSids(accountsDictionary, searchConfig.ExcludedAccounts);
      using var writer  = NewWriter(stream, ["Path", .. aclSids.Select(sid => accountsDictionary[sid].First().FullName)]);
      foreach (var matrixRow in AclMatrix.Create(resultList, aclSids))
         WriteRow(writer, [matrixRow.Path, .. matrixRow.Access.Select(a => a == AccessType.None ? string.Empty : a.ToString())]);
   }

   private void WriteGroupsCsvWithSep(Stream stream)
   {
      if (groupsList.Count == 0)
      {
         Log.Error("No groups found to export to CSV");
         return;
      }

      using var writer = NewWriter(stream, groupsList.Select(group => group[^1].FullName).ToList());
      for (var position = 0; position < groupsList.Max(group => group.Length - 1); position++)
         WriteRow(writer, groupsList.Select(group => position < group.Length - 1 ? group[position].FullName : string.Empty)
                                    .ToList());
   }

   private void WriteIdentityCsvWithSep(Stream stream)
   {
      if (accountsDictionary.IsEmpty)
      {
         Log.Error("No identity data found to export to CSV");
         return;
      }

      using var writer = NewWriter(stream, ExportData.IdentityHeaders);
      foreach (var account in accountsDictionary.Values.SelectMany(accounts => accounts))
         WriteRow(writer,
         [
                  account.FullName, account.Name ?? string.Empty, account.Domain ?? string.Empty, account.Sid.Value,
                  account.Type.ToString(), account.State.ToString(), account.Error ?? string.Empty
         ]);
   }
}
