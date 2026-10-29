// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using Serilog;
using Entree.Core.Handler;
using Entree.Core.Helpers;
using Entree.MVVM.Model;

namespace Entree.Services;

public interface ICompareService
{
   Task<List<ComparedFileSystemStructResult>> CompareFilesAsync(string originalFilePath, string compareFilePath,
                                                                CancellationToken cancellationToken = default);

   Task<string> ExportResultsAsync(bool exportAsXlsx, CancellationToken cancellationToken = default);

   void Clear();
}

public class CompareService(IAppSettings appSettings) : ICompareService
{
   private CompareHandler? _compareHandler;

   public void Clear()
   {
      _compareHandler = null;
   }

   public async Task<List<ComparedFileSystemStructResult>> CompareFilesAsync(
            string originalFilePath, string compareFilePath, CancellationToken cancellationToken = default)
   {
      if (string.IsNullOrEmpty(originalFilePath))
         throw new ArgumentException("Original file path cannot be empty", nameof(originalFilePath));
      if (string.IsNullOrEmpty(compareFilePath))
         throw new ArgumentException("Compare file path cannot be empty", nameof(compareFilePath));
      if (!File.Exists(originalFilePath)) throw new FileNotFoundException("Original file does not exist", originalFilePath);
      if (!File.Exists(compareFilePath)) throw new FileNotFoundException("Compare file does not exist", compareFilePath);

      _compareHandler = new CompareHandler(originalFilePath, compareFilePath, appSettings);
      await Task.Run(() => _compareHandler.Execute(cancellationToken), cancellationToken);
      return _compareHandler.GetResult();
   }

   public async Task<string> ExportResultsAsync(bool exportAsXlsx, CancellationToken cancellationToken = default)
   {
      try
      {
         if (_compareHandler == null)
            return string.Empty;

         var directory = PathHelper.EnsureDirectoryExists(appSettings.AppResultPath);

         var outputFilePath = PathHelper.ReserveUniqueFile("Comparison_Results", exportAsXlsx ? ".xlsx" : ".csv", directory);

         var isSuccess = false;
         try
         {
            isSuccess = await Task.Run(async () => await _compareHandler.Export(outputFilePath, exportAsXlsx, cancellationToken),
                                       cancellationToken);
         }
         finally
         {
            if (!isSuccess) PathHelper.TryDelete(outputFilePath);
         }

         return isSuccess ? outputFilePath : string.Empty;
      }
      catch (UnauthorizedAccessException ex)
      {
         Log.Error(ex, "Access denied when exporting results: {Message}", ex.Message);
         throw new
                  UnauthorizedAccessException($"Cannot write to the export directory. Check permissions for {Path.GetDirectoryName(appSettings.AppResultPath)}",
                                              ex);
      }
      catch (IOException ex)
      {
         Log.Error(ex, "IO error during export: {Message}", ex.Message);
         throw new IOException("Error writing export file. The file may be in use by another process.", ex);
      }
      catch (OutOfMemoryException ex)
      {
         Log.Error(ex, "Out of memory during export: {Message}", ex.Message);
         throw new
                  OutOfMemoryException("Not enough memory to complete the export. Try exporting as CSV instead of XLSX.",
                                       ex);
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Unexpected error during export: {Message}", ex.Message);
         throw;
      }
   }
}