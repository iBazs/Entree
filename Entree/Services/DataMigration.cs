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

   internal static bool IsVelopackRoot(string directory) =>
            File.Exists(Path.Combine(directory, "Update.exe")) || Directory.Exists(Path.Combine(directory, "current"));

   internal static void Move(string from, string to)
   {
      if (!File.Exists(Path.Combine(from, SettingsFile)) || File.Exists(Path.Combine(to, SettingsFile))) return;
      Directory.CreateDirectory(to);
      foreach (var item in Items)
      {
         var source = Path.Combine(from, item);
         var target = Path.Combine(to, item);
         if (Directory.Exists(source) && !Directory.Exists(target)) Directory.Move(source, target);
         else if (File.Exists(source) && !File.Exists(target)) File.Move(source, target);
      }

      PointLogsAt(Path.Combine(to, SettingsFile), Path.Combine(to, "Log"));
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
