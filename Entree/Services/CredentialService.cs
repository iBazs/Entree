// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Serilog;
using Entree.MVVM.Model;

namespace Entree.Services;

public interface ICredentialService
{
   Task<bool>                             AddCredentialAsync(SecureCredential credential);
   Task<bool>                             RemoveCredentialAsync(SecureCredential credential);
   ObservableCollection<SecureCredential> GetObservableCredentials();
}

public class CredentialService : ICredentialService
{
   private const int ReadAttempts       = 3;
   private const int KeptCorruptCopies = 3;

   private readonly string?                                _credentialsFilePath;
   private readonly SemaphoreSlim                          _gate                  = new(1, 1);
   private readonly ObservableCollection<SecureCredential> _observableCredentials = [];
   private          IReadOnlyList<SecureCredential>        _cachedCredentials;

   public CredentialService() : this(DefaultStorePath()) { }

   internal CredentialService(string? credentialsFilePath)
   {
      _credentialsFilePath = credentialsFilePath;
      _cachedCredentials   = credentialsFilePath is null ? [] : Load(credentialsFilePath);
      RefreshObservableCredentials();
   }

   public Task<bool> AddCredentialAsync(SecureCredential secureCredential)
   {
      if (string.IsNullOrWhiteSpace(secureCredential.DomainPath)) return Task.FromResult(false);
      return UpdateAsync(current => current.Where(c => !SameDomain(c, secureCredential))
                                           .Append(secureCredential)
                                           .ToList());
   }

   public Task<bool> RemoveCredentialAsync(SecureCredential credential) =>
            UpdateAsync(current => current.Contains(credential) ? current.Where(c => c != credential).ToList() : null);

   public ObservableCollection<SecureCredential> GetObservableCredentials() => _observableCredentials;

   private async Task<bool> UpdateAsync(Func<IReadOnlyList<SecureCredential>, IReadOnlyList<SecureCredential>?> change)
   {
      if (_credentialsFilePath is null) return StorageUnavailable();
      await _gate.WaitAsync();
      try
      {
         var updated = change(_cachedCredentials);
         if (updated is null) return false;
         if (!await Task.Run(() => TryPersist(_credentialsFilePath, updated))) return false;
         _cachedCredentials = updated;
         RefreshObservableCredentials();
         return true;
      }
      finally
      {
         _gate.Release();
      }
   }

   private void RefreshObservableCredentials()
   {
      _observableCredentials.Clear();
      foreach (var credential in _cachedCredentials) _observableCredentials.Add(credential);
   }

   private static bool SameDomain(SecureCredential a, SecureCredential b) =>
            string.Equals(a.DomainPath, b.DomainPath, StringComparison.OrdinalIgnoreCase);

   private static bool StorageUnavailable()
   {
      Log.Error("Credential storage is unavailable, so credentials cannot be saved.");
      return false;
   }

   private static string? DefaultStorePath() =>
            CredentialKey.StorageDirectory() is { } directory ? Path.Combine(directory, "d") : null;

   internal static string BackupPath(string path) => path + ".bak";

   internal static IReadOnlyList<SecureCredential> Load(string path)
   {
      foreach (var candidate in new[] { path, BackupPath(path) })
      {
         if (!File.Exists(candidate)) continue;
         var credentials = TryRead(candidate);
         if (credentials is not null)
         {
            if (candidate != path) Log.Warning("Credentials restored from backup {Backup}", candidate);
            return credentials;
         }

         Quarantine(candidate);
      }

      return [];
   }

   private static IReadOnlyList<SecureCredential>? TryRead(string path)
   {
      for (var attempt = 1; ; attempt++)
         try
         {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return Parse(stream);
         }
         catch (IOException ex) when (attempt < ReadAttempts && ex is not FileNotFoundException)
         {
            Thread.Sleep(100 * attempt);
         }
         catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or
                                          NotSupportedException)
         {
            Log.Error(ex, "Credential file {Path} is unreadable", path);
            return null;
         }
   }

   internal static IReadOnlyList<SecureCredential> Parse(Stream stream) =>
            (JsonSerializer.Deserialize<List<SecureCredential?>>(stream) ?? [])
           .OfType<SecureCredential>()
           .Where(c => !string.IsNullOrWhiteSpace(c.DomainPath))
           .ToList();

   private static void Quarantine(string path)
   {
      try
      {
         File.Copy(path, $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}", true);
         foreach (var old in CorruptCopies(path).Skip(KeptCorruptCopies)) File.Delete(old);
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Could not keep a copy of unreadable credentials file {Path}", path);
      }
   }

   internal static IEnumerable<string> CorruptCopies(string path) =>
            Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.corrupt-*").OrderDescending(StringComparer.Ordinal);

   internal static bool TryPersist(string path, IReadOnlyList<SecureCredential> credentials)
   {
      var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
      try
      {
         using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                                            FileOptions.WriteThrough))
         {
            JsonSerializer.Serialize(stream, credentials);
            stream.Flush(true);
         }

         using (var verify = File.OpenRead(tempPath))
            if (Parse(verify).Count != credentials.Count)
               throw new IOException("Written credential file did not verify");

         File.Copy(tempPath, BackupPath(path), true);
         File.Move(tempPath, path, true);
         return true;
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error saving credentials to {Path}", path);
         return false;
      }
      finally
      {
         try
         {
            if (File.Exists(tempPath)) File.Delete(tempPath);
         }
         catch (Exception ex)
         {
            Log.Warning(ex, "Could not remove temporary credential file {Path}", tempPath);
         }
      }
   }
}
