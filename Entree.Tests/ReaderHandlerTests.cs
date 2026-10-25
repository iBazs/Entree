// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.IO.Compression;
using Entree.Core.Handler;
using Xunit;

namespace Entree.Tests;

public class ReaderHandlerTests
{
   private const string CsvContent =
            "Path,Attributes,Owner,Identity,Account Rights,Last Modified Date\n" +
            "C:\\test,Normal,S-1-5-18,S-1-5-18,Read,2026-01-01 00:00:00\n";

   private static string WriteTempCsv()
   {
      var path = Path.Combine(Path.GetTempPath(), $"reader_test_{Guid.NewGuid():N}.csv");
      File.WriteAllText(path, CsvContent);
      return path;
   }

   [Fact]
   public void Execute_WithCancelledToken_ThrowsOperationCanceled()
   {
      var path = WriteTempCsv();
      try
      {
         using var cts = new CancellationTokenSource();
         cts.Cancel();

         Assert.Throws<OperationCanceledException>(() => Reader.Execute(path, cts.Token).ToList());
      }
      finally
      {
         File.Delete(path);
      }
   }

   [Fact]
   public void Execute_WithLiveToken_ReadsRows()
   {
      var path = WriteTempCsv();
      try
      {
         var rows = Reader.Execute(path, CancellationToken.None).ToList();

         Assert.Single(rows);
         Assert.Equal(@"C:\test", rows[0].Path);
      }
      finally
      {
         File.Delete(path);
      }
   }

   private static string WriteTempZip(string entryName)
   {
      var path = Path.Combine(Path.GetTempPath(), $"reader_test_{Guid.NewGuid():N}.zip");
      using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
      using var writer  = new StreamWriter(archive.CreateEntry(entryName).Open());
      writer.Write(CsvContent);
      return path;
   }

   [Fact]
   public void Execute_CsvReportZip_ReadsRawData()
   {
      var path = WriteTempZip(ExportData.RawDataCsv);
      try
      {
         var rows = Reader.Execute(path).ToList();

         Assert.Single(rows);
         Assert.Equal(@"C:\test", rows[0].Path);
      }
      finally
      {
         File.Delete(path);
      }
   }

   [Fact]
   public void Execute_ZipWithoutRawData_ThrowsFormat()
   {
      var path = WriteTempZip("Other.csv");
      try
      {
         Assert.Throws<FormatException>(() => Reader.Execute(path).ToList());
      }
      finally
      {
         File.Delete(path);
      }
   }

   [Fact]
   public void Execute_UnsupportedExtension_Throws()
   {
      Assert.Throws<NotSupportedException>(() => Reader.Execute("data.json").ToList());
   }
}
