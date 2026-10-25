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
         if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));
         if (Path.HasExtension(path)) path = Path.GetDirectoryName(path);
         if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
         return Directory.CreateDirectory(path).FullName;
      }
      catch (ArgumentNullException)
      {
         Log.Error("Save directory was empty or incorrect.");
         Log.Error("Changed save location to default at {CurrentDirectory}", Environment.CurrentDirectory);
         return Environment.CurrentDirectory;
      }
      catch (UnauthorizedAccessException)
      {
         Log.Error("Couldn't create directory at {Path}", path);
         Log.Error("Changed save location to default at {CurrentDirectory}", Environment.CurrentDirectory);
         return Environment.CurrentDirectory;
      }
   }

   public static string GenerateUniqueFileName(string? baseFileName, string extension, string? savePath)
   {
      var dateStamp        = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      var baseNameWithDate = $"{baseFileName}_{dateStamp}";
      savePath ??= EnsureDirectoryExists(savePath);
      var fullPath = Path.Combine(savePath, $"{baseNameWithDate}{extension}");

      var counter = 1;
      while (File.Exists(fullPath))
      {
         var newFileName = $"{baseNameWithDate} ({counter}){extension}";
         fullPath = Path.Combine(savePath, newFileName);
         counter++;
      }

      return Path.GetFileName(fullPath);
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
