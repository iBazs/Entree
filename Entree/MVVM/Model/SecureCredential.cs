// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.DirectoryServices.AccountManagement;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Serilog;
using Entree.Services;

namespace Entree.MVVM.Model;

public class SecureCredential
{
   [JsonPropertyName("D")]               public  string? DomainPath        { get; init; } = string.Empty;
   [JsonPropertyName("U")]               public  string  Username          { get; init; } = string.Empty;
   [JsonInclude] [JsonPropertyName("P")] private string  EncryptedPassword { get; set; }  = string.Empty;

   [JsonIgnore]
   public string Password
   {
      set
      {
         if (string.IsNullOrEmpty(value)) return;
         EncryptedPassword = Protect(value);
      }
   }

   private string GetDecryptedPassword() =>
            string.IsNullOrEmpty(EncryptedPassword) ? string.Empty : Unprotect(EncryptedPassword);

   public (bool isValid, string message) Validate()
   {
      try
      {
         using var context = CreatePrincipalContext();
         if (context == null)
            return (false, "Invalid address");

         var isValid = true;
         if (!string.IsNullOrEmpty(Username))
         {
            var password = GetDecryptedPassword();
            if (string.IsNullOrEmpty(password)) return (false, "Password required");
            isValid = context.ValidateCredentials(Username, password);
         }

         using (UserPrincipal.FindByIdentity(context, Environment.UserName)) { }

         return isValid
                  ? (true, "Connected")
                  : (false, "Invalid username or password");
      }
      catch (Exception ex)
      {
         return (false, ex.Message);
      }
   }

   private static string Protect(string plainText) =>
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), CredentialKey.Current,
                                                         DataProtectionScope.CurrentUser));

   private static string Unprotect(string encryptedText)
   {
      byte[] encryptedBytes;
      try
      {
         encryptedBytes = Convert.FromBase64String(encryptedText);
      }
      catch (FormatException ex)
      {
         throw new CryptographicException("The stored password is damaged. Enter it again in Settings.", ex);
      }

      var key = CredentialKey.Current;
      try
      {
         return Encoding.UTF8.GetString(ProtectedData.Unprotect(encryptedBytes, key,
                                                                DataProtectionScope.CurrentUser));
      }
      catch (CryptographicException ex)
      {
         throw new CryptographicException(
                  $"The stored password cannot be decrypted by Windows user {Environment.UserName}. " +
                  "It was saved by another user or computer, or is damaged. Enter it again in Settings.", ex);
      }
   }

   public PrincipalContext? CreatePrincipalContext()
   {
      try
      {
         return !string.IsNullOrEmpty(Username)
                  ? new PrincipalContext(ContextType.Domain, DomainPath, Username, GetDecryptedPassword())
                  : new PrincipalContext(ContextType.Domain, DomainPath);
      }
      catch (Exception ex)
      {
         Log.Warning("Cannot connect to {Domain}: {Message}", DomainPath, ex.Message);
         return null;
      }
   }
}
