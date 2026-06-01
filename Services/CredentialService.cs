using System;
using System.Security.Cryptography;
using System.Text;

namespace Hyper_Transmit.Services
{
    /// <summary>
    /// Service for encrypting/decrypting credentials using Windows DPAPI.
    /// </summary>
    public class CredentialService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HyperTransmit_v1.0");

        /// <summary>
        /// Encrypts a plaintext string using DPAPI (CurrentUser scope).
        /// Returns a Base64-encoded encrypted string.
        /// </summary>
        public string Encrypt(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return string.Empty;

            try
            {
                var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
                var encryptedBytes = ProtectedData.Protect(
                    plaintextBytes,
                    Entropy,
                    DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch
            {
                // Fallback: return plaintext if encryption fails
                return plaintext;
            }
        }

        /// <summary>
        /// Decrypts a Base64-encoded DPAPI-encrypted string back to plaintext.
        /// </summary>
        public string Decrypt(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64))
                return string.Empty;

            try
            {
                var encryptedBytes = Convert.FromBase64String(encryptedBase64);
                var decryptedBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    Entropy,
                    DataProtectionScope.CurrentUser);
                var result = Encoding.UTF8.GetString(decryptedBytes);

                // Handle legacy double-encrypted passwords:
                // If the decrypted result is valid Base64 and looks like DPAPI data, decrypt again
                try
                {
                    var secondBytes = Convert.FromBase64String(result);
                    if (secondBytes.Length > 0 && secondBytes.Length != result.Length)
                    {
                        var secondDecrypted = ProtectedData.Unprotect(secondBytes, Entropy, DataProtectionScope.CurrentUser);
                        return Encoding.UTF8.GetString(secondDecrypted);
                    }
                }
                catch { /* Not double-encrypted, return first result */ }

                return result;
            }
            catch
            {
                // Fallback: return as-is if decryption fails (may be legacy plaintext)
                return encryptedBase64;
            }
        }
    }
}