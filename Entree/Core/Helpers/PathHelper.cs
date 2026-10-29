// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.Globalization;
using System.IO;
using Serilog;

namespace Entree.Core.Helpers;

public static class PathHelper
{
   // Settings, saved domains and logs. The installed app has its own folder, %LocalAppData%\Entree,
   // which Velopack empties on reinstall and uninstall
   public static string DataDirectory { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Entree");

   public static string WriteableBaseDirectory()
   {
      var baseDirectory = DataDirectory;
      try
      {
         Directory.CreateDirectory(baseDirectory);
      }
      catch
      {
         baseDirectory = Environment.CurrentDirectory;
      }

      return baseDirectory;
   }

   public static string EnsureDirectoryExists(string? path)
   {
      try
      {
         if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
         return Directory.CreateDirectory(path).FullName;
      }
      catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
      {
         var fallback = WriteableBaseDirectory();
         Log.Error("Cannot use save folder {Path}, writing to {Fallback} instead: {Message}", path, fallback, ex.Message);
         return fallback;
      }
   }

   public static string ReportName(string? searchDirectory) =>
            (searchDirectory ?? string.Empty).Split(['\\', '/', ':'], StringSplitOptions.RemoveEmptyEntries) is [.., var last] &&
            last != "?"
                     ? last
                     : "Result";

   public static string ReserveUniqueFile(string baseName, string extension, string folder)
   {
      var stem = $"{baseName}_{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
      for (var counter = 0;; counter++)
      {
         var path = Path.Combine(folder, counter == 0 ? $"{stem}{extension}" : $"{stem} ({counter}){extension}");
         try
         {
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }
            return path;
         }
         catch (IOException) when (File.Exists(path)) { }
      }
   }

   public static void OpenFile(string? filePath)
   {
      var checkExistsTask = Task.Run(() => File.Exists(filePath) || Directory.Exists(filePath));
      var timeoutTask     = Task.Delay(TimeSpan.FromSeconds(3));

      var completedTask = Task.WhenAny(checkExistsTask, timeoutTask).Result;

      if (completedTask == timeoutTask)
      {
         Log.Error("No response for Path, timed out after 3 seconds: {FilePath}", filePath);
         return;
      }

      if (!checkExistsTask.Result)
      {
         Log.Error("Path not found: {FilePath}", filePath);
         return;
      }

      Log.Debug("Opening path: {FilePath}", filePath);
      using var openProcess = new Process();
      openProcess.StartInfo.FileName        = filePath;
      openProcess.StartInfo.UseShellExecute = true;
      _                                     = openProcess.Start();
   }

   public static string? ValidateAndNormalizePath(string? path)
   {
      Log.Debug("Validating path: {Path}", path);

      if (string.IsNullOrWhiteSpace(path))
      {
         Log.Error("Domain Path is null or empty");
         return null;
      }

      var normalizedPath = NormalizePath(path);
      Log.Debug("Normalized path: {path}", normalizedPath);

      return normalizedPath;
   }

   private static string NormalizePath(string? path)
   {
      if (string.IsNullOrWhiteSpace(path))
         return string.Empty;

      var normalized = path.Replace('/', '\\');

      if (normalized.StartsWith("LDAP:", StringComparison.OrdinalIgnoreCase))
      {
         normalized = normalized[5..];
      }
      else
      {
         if (normalized.StartsWith("https:", StringComparison.OrdinalIgnoreCase)) normalized = normalized[6..];
      }

      return normalized.Trim('\\');
   }

   public static void TryDelete(string path)
   {
      try
      {
         File.Delete(path);
      }
      catch (Exception ex)
      {
         Log.Warning(ex, "Could not delete partial result {File}", path);
      }
   }

   public static string ExtendedLengthPath(string path) =>
            path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
            : path.StartsWith(@"\\", StringComparison.Ordinal)  ? string.Concat(@"\\?\UNC\", path.AsSpan(2))
                                                                  : string.Concat(@"\\?\", path);
}
