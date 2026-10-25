// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Security.Cryptography;
using Serilog;
using Entree.Core.Helpers;

namespace Entree.Services;

internal static class CredentialKey
{
   private const int    KeyLength    = 32;
   private const int    ReadAttempts = 3;
   private const string KeyFileName  = "k";

   private static readonly object Gate = new();
   private static          byte[]? _key;

   public static byte[] Current
   {
      get
      {
         lock (Gate)
            return _key ??= (StorageDirectory() is { } directory ? LoadOrCreate(Path.Combine(directory, KeyFileName)) : null) ??
                            throw new CryptographicException(
                                     "The credential key of this installation is unavailable. " +
                                     $"Delete {KeyPath()} and enter the passwords again in Settings.");
      }
   }

   public static string? StorageDirectory()
   {
      if (string.IsNullOrEmpty(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))) return null;
      try
      {
         return Directory.CreateDirectory(Path.Combine(PathHelper.DataDirectory, "cache")).FullName;
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Cannot create the credential storage folder");
         return null;
      }
   }

   internal static byte[]? LoadOrCreate(string path)
   {
      if (File.Exists(path)) return Load(path);

      var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
      try
      {
         var key = RandomNumberGenerator.GetBytes(KeyLength);
         using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                                            FileOptions.WriteThrough))
         {
            stream.Write(ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser));
            stream.Flush(true);
         }

         File.Move(tempPath, path, false);
         Log.Information("Created the credential key of this installation");
         return key;
      }
      catch (IOException) when (File.Exists(path))
      {
         return Load(path);
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Cannot create the credential key {Path}", path);
         return null;
      }
      finally
      {
         try
         {
            if (File.Exists(tempPath)) File.Delete(tempPath);
         }
         catch (Exception ex)
         {
            Log.Warning(ex, "Could not remove temporary key file {Path}", tempPath);
         }
      }
   }

   private static byte[]? Load(string path)
   {
      for (var attempt = 1; ; attempt++)
         try
         {
            var key = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            if (key.Length == KeyLength) return key;
            Log.Error("Credential key {Path} has an invalid length", path);
            return null;
         }
         catch (IOException) when (attempt < ReadAttempts)
         {
            Thread.Sleep(100 * attempt);
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
         {
            Log.Error(ex, "Credential key {Path} is unreadable, damaged, or belongs to another Windows user", path);
            return null;
         }
   }

   private static string KeyPath() => Path.Combine(StorageDirectory() ?? "%LOCALAPPDATA%\\Entree\\cache", KeyFileName);
}
