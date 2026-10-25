// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Runtime.ExceptionServices;
using System.Security.Principal;
using System.Xml;
using nietras.SeparatedValues;
using Serilog;
using Entree.Core.Helpers;
using Entree.MVVM.Model;

namespace Entree.Core.Handler;

public static class Reader
{
   private const string AccessTypeHeader = "Access Type";
   private const string CsvDateFormat    = "yyyy-MM-dd HH:mm:ss";
   private const double FirstSerialAfterLeapBug = 61;

   private static readonly DateTime ExcelEpoch = new(1899, 12, 30);

   public static IEnumerable<FlatFileSystemStruct> Execute(string filePath, CancellationToken cancellationToken = default)
   {
      var extension = Path.GetExtension(filePath).ToLowerInvariant();
      return extension switch
             {
                      ".xlsx" => ReadExcelFileData(filePath, cancellationToken),
                      ".csv"  => Guard(ReadCsvFileData(filePath, cancellationToken), Path.GetFileName(filePath)),
                      ".zip"  => Guard(ReadZipFileData(filePath, cancellationToken), Path.GetFileName(filePath)),
                      _       => throw new NotSupportedException($"Unsupported file extension: {extension}")
             };
   }

   private static bool IsMalformedContent(Exception ex) =>
            ex is XmlException or InvalidDataException;

   private static FormatException Malformed(string source, Exception inner) =>
            new($"{source} could not be read: {inner.Message}", inner);

   private static IEnumerable<T> Guard<T>(IEnumerable<T> source, string context)
   {
      using var enumerator = source.GetEnumerator();
      while (true)
      {
         bool hasNext;
         try
         {
            hasNext = enumerator.MoveNext();
         }
         catch (Exception ex) when (IsMalformedContent(ex))
         {
            throw Malformed(context, ex);
         }

         if (!hasNext) yield break;
         yield return enumerator.Current;
      }
   }

   internal static AccessControlType ParseAccessType(string? value) =>
            Enum.TryParse<AccessControlType>(value, true, out var type) ? type : AccessControlType.Allow;

   internal static DateTime ParseDate(string? value)
   {
      if (string.IsNullOrWhiteSpace(value)) return DateTime.MinValue;
      if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
         return FromExcelSerial(serial);
      if (DateTime.TryParseExact(value, CsvDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
          DateTime.TryParse(value, out date))
         return TruncateToSeconds(date);
      return DateTime.MinValue;
   }

   internal static DateTime FromExcelSerial(double serial)
   {
      var origin = serial >= FirstSerialAfterLeapBug ? ExcelEpoch : ExcelEpoch.AddDays(1);
      var ticks  = Math.Round(serial * TimeSpan.TicksPerDay / TimeSpan.TicksPerMicrosecond) * TimeSpan.TicksPerMicrosecond;
      var limit  = DateTime.MaxValue.Ticks - origin.Ticks;
      if (!(ticks >= -origin.Ticks && ticks < limit)) return DateTime.MinValue;
      var offset = (long)ticks;
      return offset > limit ? DateTime.MinValue : TruncateToSeconds(origin.AddTicks(offset));
   }

   private static DateTime TruncateToSeconds(DateTime value) =>
            new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);

   private static IEnumerable<FlatFileSystemStruct> ReadExcelFileData(string filePath,
                                                                      CancellationToken cancellationToken)
   {
      var          fileName = Path.GetFileName(filePath);
      XlsxWorkbook workbook;
      try
      {
         workbook = XlsxSheetReader.Load(filePath);
      }
      catch (Exception ex) when (IsMalformedContent(ex))
      {
         throw Malformed(fileName, ex);
      }

      var sheetNames = workbook.Sheets.Select(s => s.Name).Where(x => x.Contains("RawData")).ToList();
      if (sheetNames.Count == 0)
         throw new FormatException($"{fileName} has no RawData sheet, so it is not an Entrée report.");
      Log.Information("Pre-Processing {SheetCount} sheets from {FilePath}", sheetNames.Count, filePath);

      try
      {
         return sheetNames.AsParallel()
                          .AsOrdered()
                          .WithCancellation(cancellationToken)
                          .Select(sheetName => Guard(ProcessSheet(workbook, sheetName, cancellationToken),
                                                     $"{fileName}, sheet {sheetName}").ToList())
                          .ToList()
                          .SelectMany(rows => rows);
      }
      catch (AggregateException ae)
      {
         var inner = ae.Flatten().InnerExceptions;
         ExceptionDispatchInfo.Capture(inner.FirstOrDefault(e => e is OperationCanceledException) ?? inner[0]).Throw();
         throw;
      }
   }

   private static IEnumerable<FlatFileSystemStruct> ProcessSheet(XlsxWorkbook workbook, string sheetName,
                                                                 CancellationToken cancellationToken)
   {
      Log.Information("Reading sheet {SheetName}", sheetName);
      using var rows = XlsxSheetReader.ReadRows(workbook, sheetName, cancellationToken).GetEnumerator();
      if (!rows.MoveNext() || rows.Current.Length == 0)
      {
         Log.Warning("No content, or missing header in sheet {SheetName}", sheetName);
         yield break;
      }

      var columnMap = ColumnMap.FromHeader(rows.Current);
      columnMap.Validate(sheetName);

      var parser   = new RowParser(columnMap, sheetName);
      var rowIndex = 2;
      while (rows.MoveNext())
      {
         var parsedRow = parser.Parse(rows.Current, rowIndex++);
         if (!string.IsNullOrEmpty(parsedRow.Path)) yield return parsedRow;
      }
   }

   private static IEnumerable<FlatFileSystemStruct> ReadCsvFileData(string filePath, CancellationToken cancellationToken)
   {
      using var stream = SharedFile.OpenRead(filePath);
      foreach (var row in ReadCsvStream(stream, filePath, cancellationToken)) yield return row;
   }

   private static IEnumerable<FlatFileSystemStruct> ReadZipFileData(string filePath, CancellationToken cancellationToken)
   {
      using var stream  = SharedFile.OpenRead(filePath);
      using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
      var entry = archive.GetEntry(ExportData.RawDataCsv) ??
                  throw new FormatException($"{Path.GetFileName(filePath)} has no {ExportData.RawDataCsv}, so it is not an Entrée report.");
      using var entryStream = entry.Open();
      foreach (var row in ReadCsvStream(entryStream, filePath, cancellationToken)) yield return row;
   }

   private static IEnumerable<FlatFileSystemStruct> ReadCsvStream(Stream stream, string filePath,
                                                                  CancellationToken cancellationToken)
   {
      using var reader = Sep.Reader(options => options with { HasHeader = true, Unescape = true }).From(stream);
      Log.Information("Reading {FilePath}", filePath);

      var columnMap = ColumnMap.FromHeader(reader.Header.ColNames.ToArray());
      columnMap.Validate(Path.GetFileName(filePath));

      var parser   = new RowParser(columnMap, null);
      var cells    = new string?[columnMap.Width];
      var rowIndex = 2;
      foreach (var row in reader)
      {
         cancellationToken.ThrowIfCancellationRequested();
         for (var i = 0; i < cells.Length; i++) cells[i] = i < row.ColCount ? ExportData.FromCsvCell(row[i].ToString()) : null;
         var parsedRow = parser.Parse(cells, rowIndex++);
         if (!string.IsNullOrEmpty(parsedRow.Path)) yield return parsedRow;
      }
   }

   private sealed record ColumnMap(int Path, int Attributes, int Owner, int Identity, int Access, int AccessType,
                                   int LastWrite)
   {
      public int Width => new[] { Path, Attributes, Owner, Identity, Access, AccessType, LastWrite }.Max() + 1;

      public static ColumnMap FromHeader(IReadOnlyList<string?> header)
      {
         int path = -1, attributes = -1, owner = -1, identity = -1, access = -1, accessType = -1, lastWrite = -1;
         for (var i = 0; i < header.Count; i++)
         {
            var name = header[i]?.Trim() ?? string.Empty;
            if (name.Length == 0) continue;
            if (name.Contains("Path", StringComparison.OrdinalIgnoreCase)) path = i;
            else if (name.Contains("Attributes", StringComparison.OrdinalIgnoreCase)) attributes = i;
            else if (name.Contains("Owner", StringComparison.OrdinalIgnoreCase)) owner = i;
            else if (name.Contains("Identity", StringComparison.OrdinalIgnoreCase)) identity = i;
            else if (name.Equals(AccessTypeHeader, StringComparison.OrdinalIgnoreCase)) accessType = i;
            else if (name.Contains("Access", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Rights", StringComparison.OrdinalIgnoreCase)) access = i;
            else if (name.Contains("Modified Date", StringComparison.OrdinalIgnoreCase)) lastWrite = i;
         }

         return new ColumnMap(path, attributes, owner, identity, access, accessType, lastWrite);
      }

      public void Validate(string source)
      {
         var missing = new (string Name, int Index)[]
                       {
                                ("Path", Path), ("Attributes", Attributes), ("Identity", Identity), ("Access", Access),
                                ("LastWrite", LastWrite)
                       }
                      .Where(c => c.Index < 0)
                      .Select(c => c.Name)
                      .ToList();
         if (missing.Count == 0) return;
         throw new FormatException($"{source} is missing required columns: {string.Join(", ", missing)}");
      }
   }

   private sealed class RowParser(ColumnMap map, string? sheetName)
   {
      private readonly Dictionary<string, FileAttributes>      _attributes = new(StringComparer.Ordinal);
      private readonly Dictionary<string, FileSystemRights>    _rights     = new(StringComparer.Ordinal);
      private readonly Dictionary<string, SecurityIdentifier?> _sids       = new(StringComparer.Ordinal);
      private          DateTime                                _lastDate;
      private          string?                                 _lastDateText;
      private          string?                                 _lastPath;

      public FlatFileSystemStruct Parse(string?[] cells, int rowIndex)
      {
         var path = Cell(cells, map.Path) ?? string.Empty;
         if (path == _lastPath) path = _lastPath;
         else _lastPath = path;

         var dateText = Cell(cells, map.LastWrite);
         if (dateText != _lastDateText)
         {
            _lastDateText = dateText;
            _lastDate     = ParseDate(dateText);
         }

         return new FlatFileSystemStruct
                {
                         Path           = path,
                         FileAttributes = Enum(_attributes, Cell(cells, map.Attributes), rowIndex),
                         Owner          = Sid(Cell(cells, map.Owner), rowIndex),
                         Identity       = Sid(Cell(cells, map.Identity), rowIndex),
                         Access         = Enum(_rights, Cell(cells, map.Access), rowIndex),
                         AccessType     = ParseAccessType(Cell(cells, map.AccessType)),
                         LastWriteTime  = _lastDate
                };
      }

      private static string? Cell(string?[] cells, int index) => index >= 0 && index < cells.Length ? cells[index] : null;

      private SecurityIdentifier? Sid(string? value, int rowIndex)
      {
         if (string.IsNullOrEmpty(value)) return null;
         if (_sids.TryGetValue(value, out var sid)) return sid;
         sid = ReportHelper.ParseSecurityIdentifier(value, rowIndex, sheetName);
         _sids[value] = sid;
         return sid;
      }

      private T Enum<T>(Dictionary<string, T> cache, string? value, int rowIndex) where T : struct, Enum
      {
         if (string.IsNullOrWhiteSpace(value)) return default;
         if (cache.TryGetValue(value, out var parsed)) return parsed;
         parsed       = ReportHelper.ParseEnum<T>(value, rowIndex, sheetName);
         cache[value] = parsed;
         return parsed;
      }
   }
}
