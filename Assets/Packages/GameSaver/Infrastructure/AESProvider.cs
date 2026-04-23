using System;
using System.IO;
using System.Security.Cryptography;
using ThanhDV.GameSaver.Core;

namespace ThanhDV.GameSaver.Infrastructure
{
    /// <summary>
    /// Provides AES encryption and decryption capabilities.
    /// </summary>
    public class AESProvider : IEncryptionProvider
    {
        private readonly byte[] _keyBytes;

        public AESProvider(string secretKey)
        {
            if (string.IsNullOrEmpty(secretKey)) throw new ArgumentNullException(nameof(secretKey), "The secret key cannot be null or empty.");
            _keyBytes = DeriveKey(secretKey);
        }

        /// <summary>
        /// Encrypts the specified plain text using AES.
        /// </summary>
        /// <param name="plainText">The string to encrypt.</param>
        /// <returns>A Base64 encoded string representing the encrypted data.</returns>
        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            try
            {
                using Aes aes = Aes.Create();
                aes.Key = _keyBytes;
                aes.GenerateIV();
                byte[] iv = aes.IV;

                using ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using MemoryStream msEncrypt = new();
                msEncrypt.Write(iv, 0, iv.Length);

                using (CryptoStream csEncrypt = new(msEncrypt, encryptor, CryptoStreamMode.Write))
                using (StreamWriter swEncrypt = new(csEncrypt))
                {
                    swEncrypt.Write(plainText);
                }

                return Convert.ToBase64String(msEncrypt.ToArray());
            }
            catch (Exception e)
            {
                throw new CryptographicException($"Error during data encryption: {e.Message}");
            }
        }

        /// <summary>
        /// Decrypts the specified cipher text using AES.
        /// </summary>
        /// <param name="cipherText">The Base64 encoded string to decrypt.</param>
        /// <returns>The decrypted plain text.</returns>
        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            try
            {
                byte[] fullCipher = Convert.FromBase64String(cipherText);

                using Aes aes = Aes.Create();
                aes.Key = _keyBytes;

                byte[] iv = new byte[16];
                Array.Copy(fullCipher, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using MemoryStream msDecrypt = new(fullCipher, iv.Length, fullCipher.Length - iv.Length);
                using CryptoStream csDecrypt = new(msDecrypt, decryptor, CryptoStreamMode.Read);
                using StreamReader srDecrypt = new(csDecrypt);

                return srDecrypt.ReadToEnd();
            }
            catch (CryptographicException)
            {
                throw new CryptographicException("Decryption failed. Incorrect Secret Key or the data has been corrupted/tampered with.");
            }
            catch (Exception e)
            {
                throw new CryptographicException($"Unknown error during decryption: {e.Message}", e);
            }
        }

        /// <summary>
        /// Derives a 256-bit secure key from the provided string using SHA256 hashing.
        /// </summary>
        /// <param name="secretKey">The user-provided secret key string.</param>
        /// <returns>A 32-byte array representing the derived key.</returns>
        private byte[] DeriveKey(string secretKey)
        {
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(secretKey));
        }
    }
}