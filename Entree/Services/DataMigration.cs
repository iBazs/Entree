// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Entree.Core.Helpers;

namespace Entree.Services;

// Up to 0.9.0 the data lived in %LocalAppData%\Entree, which is now the installed app's folder.
// It moves to PathHelper.DataDirectory before Velopack's Setup or uninstall can delete it.
internal static partial class DataMigration
{
   private const  string   SettingsFile = "appsettings.json";
   private const  string   MutexName    = @"Local\EntreeDataMigration";
   private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);
   private static readonly string[] Items = [SettingsFile, "cache", "Log"];

   public static string LegacyDirectory { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Entree");

   // A portable build moves the data out itself, unless the folder is already Velopack's install
   public static void FromPortable()
   {
      if (IsVelopackRoot(LegacyDirectory)) return;
      Move(LegacyDirectory, PathHelper.DataDirectory);
      try
      {
         if (Directory.Exists(LegacyDirectory) && !Directory.EnumerateFileSystemEntries(LegacyDirectory).Any())
            Directory.Delete(LegacyDirectory);
      }
      catch (IOException) { }
   }

   // Installing over the old folder makes Setup rename it to Entree.<16 random characters> and delete it once
   // the install hook has run, so the hook takes the data from there
   public static void FromSetupBackup()
   {
      var parent = Path.GetDirectoryName(LegacyDirectory);
      if (parent is null || !Directory.Exists(parent)) return;
      var backup = new DirectoryInfo(parent).EnumerateDirectories("Entree.*")
                                            .Where(d => SetupBackupName().IsMatch(d.Name) &&
                                                        File.Exists(Path.Combine(d.FullName, SettingsFile)))
                                            .MaxBy(d => d.LastWriteTimeUtc);
      if (backup is not null) Move(backup.FullName, PathHelper.DataDirectory);
   }

   public static void Safely(Action migrate)
   {
      try
      {
         migrate();
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
         Console.Error.WriteLine($"Moving the Entrée data stopped and is retried on the next start: {ex.Message}");
      }
   }

   internal static bool IsVelopackRoot(string directory) =>
            File.Exists(Path.Combine(directory, "Update.exe")) || Directory.Exists(Path.Combine(directory, "current"));

   internal static void Move(string from, string to)
   {
      using var gate = new Mutex(false, MutexName);
      bool      owned;
      try
      {
         owned = gate.WaitOne(GateTimeout);
      }
      catch (AbandonedMutexException)
      {
         owned = true;
      }

      try
      {
         if (!Directory.Exists(from)) return;
         Directory.CreateDirectory(to);
         foreach (var item in Items.Reverse()) MoveItem(Path.Combine(from, item), Path.Combine(to, item));
         if (File.Exists(Path.Combine(to, SettingsFile)))
            PointLogsAt(Path.Combine(to, SettingsFile), Path.Combine(to, "Log"));
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
      {
         Console.Error.WriteLine($"Moving the Entrée data from {from} to {to} stopped and is retried on the next start: {ex.Message}");
      }
      finally
      {
         if (owned) gate.ReleaseMutex();
      }
   }

   private static void MoveItem(string source, string target)
   {
      if (File.Exists(source))
      {
         if (!File.Exists(target)) MoveFile(source, target);
         return;
      }

      if (!Directory.Exists(source)) return;
      if (!Directory.Exists(target) || IsCoveredBy(target, source))
      {
         if (Directory.Exists(target)) Directory.Delete(target, true);
         MoveDirectory(source, target);
         return;
      }

      foreach (var file in Directory.EnumerateFiles(source))
      {
         var moved = Path.Combine(target, Path.GetFileName(file));
         if (!File.Exists(moved)) MoveFile(file, moved);
      }
   }

   internal static bool IsCoveredBy(string target, string source)
   {
      var names = Directory.EnumerateFiles(target).Select(Path.GetFileName).ToList();
      return names.Count < Directory.EnumerateFiles(source).Count() &&
             names.All(name => File.Exists(Path.Combine(source, name!)));
   }

   private static void MoveFile(string source, string target)
   {
      File.Copy(source, target, false);
      File.Delete(source);
   }

   private static void MoveDirectory(string source, string target)
   {
      var staging = $"{target}.{Guid.NewGuid():N}.partial";
      CopyDirectory(source, staging);
      Directory.Move(staging, target);
      Directory.Delete(source, true);
   }

   private static void CopyDirectory(string source, string target)
   {
      Directory.CreateDirectory(target);
      foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
      foreach (var folder in Directory.EnumerateDirectories(source))
         CopyDirectory(folder, Path.Combine(target, Path.GetFileName(folder)));
   }

   // The old default log folder was saved as an absolute path inside the old folder
   private static void PointLogsAt(string settingsPath, string logDirectory)
   {
      var settings = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
      var oldLogs  = Path.Combine(LegacyDirectory, "Log");
      if (settings?["AppLogPath"]?.GetValue<string>() is not { } logPath ||
          !string.Equals(Path.TrimEndingDirectorySeparator(logPath), oldLogs, StringComparison.OrdinalIgnoreCase))
         return;
      settings["AppLogPath"] = logDirectory;
      File.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
   }

   [GeneratedRegex("^Entree\\.[A-Za-z0-9]{16}$")]
   private static partial Regex SetupBackupName();
}
