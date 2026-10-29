// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using LargeXlsx;
using nietras.SeparatedValues;
using Serilog;
using Entree.Core.Helpers;
using Entree.MVVM.Model;

namespace Entree.Core.Handler;

public class CompareHandler(
         string originalPath,
         string newPath,
         IAppSettings appSettings)
{
   private static readonly string[] CompareHeaders =
   [
            "Path", "Original Owner", "New Owner", "Original Attributes", "New Attributes", "Identity", "Access Type",
            "Original Account Rights", "New Account Rights", "Modified Date", "New Modified Date", "Difference"
   ];

   private bool           _ignoreOwners;
   private DifferenceKind _differenceKinds;

   private List<ComparedFileSystemStructResult> _resultList = [];

   public List<ComparedFileSystemStructResult> GetResult() => _resultList;

   public async Task Execute(CancellationToken cancellationToken = default)
   {
      Log.Information("Comparing {Original} with {New}", originalPath, newPath);
      var originalTask = Task.Run(() => GroupByPath(Reader.Execute(originalPath, cancellationToken)), cancellationToken);
      var currentTask  = Task.Run(() => GroupByPath(Reader.Execute(newPath, cancellationToken)), cancellationToken);
      var original     = await originalTask;
      var current      = await currentTask;
      _ignoreOwners = !HasOwners(original) || !HasOwners(current);

      _resultList = current.Values
                           .AsParallel()
                           .WithCancellation(cancellationToken)
                           .WithDegreeOfParallelism(Parallelism.Degree(appSettings.ThreadLimit))
                           .SelectMany(group => CompareGroup(original, group))
                           .AsSequential()
                           .Concat(original.Where(o => !current.ContainsKey(o.Key))
                                           .SelectMany(o => o.Value)
                                           .Select(row => FromRow(row, "Missing")))
                           .OrderBy(result => result.Path, StringComparer.Ordinal)
                           .ToList();
      _differenceKinds = _resultList.Aggregate(DifferenceKind.None, (kinds, result) => kinds | result.Kind);

      Log.Information("Comparison complete. Found {Count} differences", _resultList.Count);
   }

   public Task<bool> Export(string outputPath, bool exportAsXlsx, CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(outputPath);
      ArgumentNullException.ThrowIfNull(_resultList);
      if (_resultList.Count == 0)
      {
         Log.Warning("No differences found to export");
         Console.WriteLine("No differences found to export");
         return Task.FromResult(false);
      }

      try
      {
         if (exportAsXlsx) ExportAsExcel(outputPath, cancellationToken);
         else ExportAsCsv(outputPath, cancellationToken);
      }
      catch
      {
         PathHelper.TryDelete(outputPath);
         throw;
      }

      Log.Debug("Exported {Count} differences to {Path}", _resultList.Count, outputPath);
      return Task.FromResult(true);
   }

   private void ExportAsCsv(string outputPath, CancellationToken cancellationToken)
   {
      using var    writer     = Sep.Writer(o => o with { Escape = true }).ToFile(outputPath);
      const string dateFormat = "yyyy-MM-dd HH:mm:ss";

      foreach (var result in _resultList)
      {
         cancellationToken.ThrowIfCancellationRequested();
         using var writeRow = writer.NewRow();
         writeRow[CompareHeaders[0]].Set(ExportData.CsvCell(result.Path));
         writeRow[CompareHeaders[1]].Set(ExportData.CsvCell(result.Owner?.ToString()));
         if (_differenceKinds.HasFlag(DifferenceKind.Owner)) writeRow[CompareHeaders[2]].Set(ExportData.CsvCell(result.NewOwner?.ToString()));
         writeRow[CompareHeaders[3]].Set(ExportData.CsvCell(result.FileEntryAttributes.ToString()));
         if (_differenceKinds.HasFlag(DifferenceKind.Attributes))
            writeRow[CompareHeaders[4]].Set(ExportData.CsvCell(result.NewFileEntryAttributes.ToString()));
         writeRow[CompareHeaders[5]].Set(ExportData.CsvCell(result.Identity?.ToString()));
         writeRow[CompareHeaders[6]].Set(ExportData.CsvCell(result.AccessType.ToString()));
         writeRow[CompareHeaders[7]].Set(ExportData.CsvCell(result.Access.ToString()));
         if (_differenceKinds.HasFlag(DifferenceKind.Access)) writeRow[CompareHeaders[8]].Set(ExportData.CsvCell(result.NewAccess.ToString()));
         writeRow[CompareHeaders[9]].Set(result.LastWriteTime.ToString(dateFormat, CultureInfo.InvariantCulture));
         if (_differenceKinds.HasFlag(DifferenceKind.Date))
            writeRow[CompareHeaders[10]].Set(result.NewLastWriteTime == DateTime.MinValue
                                                     ? string.Empty
                                                     : result.NewLastWriteTime.ToString(dateFormat, CultureInfo.InvariantCulture));

         writeRow[CompareHeaders[11]].Set(ExportData.CsvCell(result.Difference));
      }

      writer.Flush();
   }

   private void ExportAsExcel(string outputPath, CancellationToken cancellationToken)
   {
      using var fileStream = File.Create(outputPath);
      using var excel = new XlsxWriter(fileStream, XlsxCompressionLevel.Optimal, requireCellReferences: false);

      var currentHeaders = CompareHeaders.ToList();
      if (!_differenceKinds.HasFlag(DifferenceKind.Owner)) currentHeaders.Remove("New Owner");
      if (!_differenceKinds.HasFlag(DifferenceKind.Attributes)) currentHeaders.Remove("New Attributes");
      if (!_differenceKinds.HasFlag(DifferenceKind.Access)) currentHeaders.Remove("New Account Rights");
      if (!_differenceKinds.HasFlag(DifferenceKind.Date)) currentHeaders.Remove("New Modified Date");

      var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
      var pages   = _resultList.Chunk(ReportHelper.MaxRows).ToList();
      for (var page = 0; page < pages.Count; page++)
      {
         excel.BeginWorksheet(page == 0 ? "Comparison Results" : $"Comparison Results_{page + 1}")
              .SetAutoFilter(6, 1, pages[page].Length + 1, currentHeaders.Count);
         excel.BeginRow();
         excel.Write("Comparison result: " + created);
         excel.BeginRow();
         excel.Write("Original file: " + originalPath);
         excel.BeginRow();
         excel.Write("New file " + newPath);
         excel.BeginRow();
         excel.BeginRow();
         excel.BeginRow();

         foreach (var header in currentHeaders)
            excel.Write(header, ReportHelper.HeaderStyle);

         foreach (var result in pages[page]) WriteResult(excel, result, cancellationToken);
      }
   }

   private void WriteResult(XlsxWriter excel, ComparedFileSystemStructResult result, CancellationToken cancellationToken)
   {
      cancellationToken.ThrowIfCancellationRequested();
      excel.BeginRow();
      excel.Write(result.Path);
      excel.Write(result.Owner?.ToString());
      if (_differenceKinds.HasFlag(DifferenceKind.Owner)) excel.Write(result.NewOwner?.ToString());
      excel.Write(result.FileEntryAttributes.ToString());
      if (_differenceKinds.HasFlag(DifferenceKind.Attributes)) excel.Write(result.NewFileEntryAttributes.ToString());
      excel.Write(result.Identity?.ToString());
      excel.Write(result.AccessType.ToString());
      var access = result.Access.ToString();
      excel.Write(access == "0" ? string.Empty : access);
      if (_differenceKinds.HasFlag(DifferenceKind.Access)) excel.Write(result.NewAccess.ToString());
      excel.Write(result.LastWriteTime, XlsxStyle.Default.With(XlsxNumberFormat.ShortDateTime));
      if (_differenceKinds.HasFlag(DifferenceKind.Date))
      {
         if (result.NewLastWriteTime == DateTime.MinValue) excel.Write();
         else excel.Write(result.NewLastWriteTime, XlsxStyle.Default.With(XlsxNumberFormat.ShortDateTime));
      }

      excel.Write(result.Difference);
   }

   private List<ComparedFileSystemStructResult> CompareGroup(Dictionary<string, List<FlatFileSystemStruct>> original,
                                                             IReadOnlyList<FlatFileSystemStruct> group)
   {
      if (!original.TryGetValue(group[0].Path, out var originals))
         return group.Select(row => FromRow(row, "New")).ToList();
      return group.Select((row, index) => CompareFileSystemItem(original, row, index == 0))
                  .OfType<ComparedFileSystemStructResult>()
                  .Concat(originals.Where(o => o.Identity is not null &&
                                               !group.Any(c => c.Identity == o.Identity && c.AccessType == o.AccessType))
                                   .Select(o => FromRow(o, "Removed Identity")))
                  .ToList();
   }

   private static ComparedFileSystemStructResult FromRow(FlatFileSystemStruct row, string difference) =>
            new()
            {
                     Path                = row.Path,
                     Owner               = row.Owner,
                     FileEntryAttributes = row.FileAttributes,
                     Identity            = row.Identity,
                     AccessType          = row.AccessType,
                     Access              = row.Access,
                     LastWriteTime       = row.LastWriteTime,
                     Difference          = difference
            };

   internal static bool HasOwners(Dictionary<string, List<FlatFileSystemStruct>> rows) =>
            rows.Values.Any(list => list.Exists(row => row.Owner is not null));

   internal static Dictionary<string, List<FlatFileSystemStruct>> GroupByPath(IEnumerable<FlatFileSystemStruct> rows)
   {
      var lookup = new Dictionary<string, List<FlatFileSystemStruct>>(StringComparer.OrdinalIgnoreCase);
      foreach (var row in rows)
      {
         ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(lookup, row.Path, out _);
         (list ??= new List<FlatFileSystemStruct>(4)).Add(row);
      }

      return lookup;
   }

   internal ComparedFileSystemStructResult? CompareFileSystemItem(
            Dictionary<string, List<FlatFileSystemStruct>> originalFileLookup,
            FlatFileSystemStruct comparisonItem, bool firstRowOfPath = true)
   {
      if (string.IsNullOrEmpty(comparisonItem.Path)) return null;

      if (!originalFileLookup.TryGetValue(comparisonItem.Path, out var matchingOriginals)) return null;
      if (matchingOriginals.Count == 0) return null;

      var primaryOriginal = matchingOriginals.First();
      var result = new ComparedFileSystemStructResult
                   { Path = comparisonItem.Path, AccessType = comparisonItem.AccessType };

      var differences = new List<string>(4);
      var kind        = DifferenceKind.None;

      var matchingIdentityStruct = new FlatFileSystemStruct();
      var matchingOwnerStruct    = new FlatFileSystemStruct();

      foreach (var original in matchingOriginals)
      {
         if (string.IsNullOrEmpty(matchingIdentityStruct.Path) &&
             original.Identity == comparisonItem.Identity &&
             original.AccessType == comparisonItem.AccessType)
            matchingIdentityStruct = original;

         if (string.IsNullOrEmpty(matchingOwnerStruct.Path) &&
             (_ignoreOwners || original.Owner == comparisonItem.Owner))
            matchingOwnerStruct = original;

         if (!string.IsNullOrEmpty(matchingIdentityStruct.Path) &&
             !string.IsNullOrEmpty(matchingOwnerStruct.Path))
            break;
      }

      if (string.IsNullOrEmpty(matchingIdentityStruct.Path))
      {
         differences.Add("New Identity");
         kind             |= DifferenceKind.Access;
         result.Identity  =  comparisonItem.Identity;
         result.NewAccess =  comparisonItem.Access;
      }
      else
      {
         result.Identity = matchingIdentityStruct.Identity;
         if (matchingIdentityStruct.Access != comparisonItem.Access)
         {
            differences.Add("New Access");
            kind             |= DifferenceKind.Access;
            result.Access    = matchingIdentityStruct.Access;
            result.NewAccess = comparisonItem.Access;
         }
         else
         {
            result.Access = matchingIdentityStruct.Access;
         }
      }

      if (firstRowOfPath && string.IsNullOrEmpty(matchingOwnerStruct.Path))
      {
         differences.Add("New Owner");
         kind            |= DifferenceKind.Owner;
         result.Owner    = primaryOriginal.Owner;
         result.NewOwner = comparisonItem.Owner;
      }
      else
      {
         result.Owner = matchingOwnerStruct.Owner;
      }

      if (firstRowOfPath && !Equals(comparisonItem.FileAttributes, primaryOriginal.FileAttributes))
      {
         differences.Add("New Attributes");
         kind                          |= DifferenceKind.Attributes;
         result.FileEntryAttributes    = primaryOriginal.FileAttributes;
         result.NewFileEntryAttributes = comparisonItem.FileAttributes;
      }
      else
      {
         result.FileEntryAttributes = primaryOriginal.FileAttributes;
      }

      if (firstRowOfPath && primaryOriginal.LastWriteTime != comparisonItem.LastWriteTime)
      {
         differences.Add("Date Changed");
         kind                    |= DifferenceKind.Date;
         result.LastWriteTime    = primaryOriginal.LastWriteTime;
         result.NewLastWriteTime = comparisonItem.LastWriteTime;
      }
      else
      {
         result.LastWriteTime = primaryOriginal.LastWriteTime;
      }

      if (differences.Count == 0) return null;

      result.Kind       = kind;
      result.Difference = string.Join(", ", differences);
      return result;
   }
}