using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;

namespace ThanhDV.SaveKeeper.Infrastructure
{
    /// <summary>
    /// AES-256-CBC encryption with HMAC-SHA256 authentication (Encrypt-then-MAC pattern).
    /// Provides both confidentiality (AES) and integrity (HMAC) — tampered ciphertext is rejected
    /// at decrypt time rather than silently returning corrupt plaintext.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: Do NOT change the master key after shipping. Saves encrypted with the previous
    /// key cannot be decrypted by a new key — players will lose progress. The library does not
    /// support key rotation by design: for client-side game saves, any attacker capable of
    /// decompiling the binary already has the key, so rotation is security theater.
    /// </remarks>
    public class AESProvider : IEncryptionProvider
    {
        private const int IV_SIZE = 16;
        private const int TAG_SIZE = 32;
        private const int RECOMMENDED_MIN_KEY_LENGTH = 16;

        private readonly KeyMaterial _material;

        /// <summary>
        /// Initializes an AES provider with a master key for encryption and authentication.
        /// </summary>
        /// <param name="masterKey">A secret key (16+ characters recommended; GUID format is suitable).</param>
        /// <exception cref="ArgumentException">Thrown if the key is null, empty, or whitespace.</exception>
        public AESProvider(string masterKey)
        {
            if (string.IsNullOrWhiteSpace(masterKey))
            {
                throw new ArgumentException("The master key cannot be null, empty, or whitespace.", nameof(masterKey));
            }

            if (masterKey.Length < RECOMMENDED_MIN_KEY_LENGTH)
            {
                DebugLog.Warning($"AESProvider has a weak key ({masterKey.Length} chars). Use at least {RECOMMENDED_MIN_KEY_LENGTH} high-entropy characters (e.g., GUID).");
            }

            _material = DeriveKeyMaterial(masterKey);
        }

        /// <summary>
        /// Encrypts and authenticates the specified plain text.
        /// Output format: Base64( iv | ciphertext | hmac tag ).
        /// </summary>
        /// <param name="plainText">Text to encrypt. Returns unchanged if null or empty.</param>
        /// <returns>Base64-encoded authenticated ciphertext.</returns>
        /// <exception cref="CryptographicException">Wraps any underlying encryption failure.</exception>
        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            try
            {
                using Aes aes = Aes.Create();
                aes.Key = _material.EncKey;
                aes.GenerateIV();
                byte[] iv = aes.IV;

                using ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, iv);
                using MemoryStream msEncrypt = new();

                // Write IV at the start
                msEncrypt.Write(iv, 0, IV_SIZE);

                // Encrypt and append ciphertext
                using (CryptoStream csEncrypt = new(msEncrypt, encryptor, CryptoStreamMode.Write))
                using (StreamWriter swEncrypt = new(csEncrypt))
                {
                    swEncrypt.Write(plainText);
                }

                // Capture iv + ciphertext (msEncrypt is now closed because CryptoStream's dispose
                // cascades to the underlying stream — but MemoryStream.ToArray() still works post-close).
                byte[] ivAndCipher = msEncrypt.ToArray();

                // Compute HMAC over (iv || ciphertext) — Encrypt-then-MAC, the secure ordering
                byte[] tag = ComputeHmac(ivAndCipher, _material.MacKey);

                // Assemble final payload in a fresh buffer (cannot write to closed msEncrypt).
                byte[] result = new byte[ivAndCipher.Length + TAG_SIZE];
                Buffer.BlockCopy(ivAndCipher, 0, result, 0, ivAndCipher.Length);
                Buffer.BlockCopy(tag, 0, result, ivAndCipher.Length, TAG_SIZE);

                return Convert.ToBase64String(result);
            }
            catch (Exception e)
            {
                throw new CryptographicException($"Error during data encryption: {e.Message}", e);
            }
        }

        /// <summary>
        /// Decrypts and authenticates a Base64 string produced by <see cref="Encrypt"/>.
        /// </summary>
        /// <param name="cipherText">Base64-encoded ciphertext. Returns unchanged if null or empty.</param>
        /// <returns>Original plain text.</returns>
        /// <exception cref="CryptographicException">
        /// Thrown when the data is too short, the HMAC tag fails to verify (tamper or wrong key),
        /// or any underlying decryption error occurs. Bad key and tampering are intentionally
        /// indistinguishable — preventing oracle attacks.
        /// </exception>
        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            try
            {
                byte[] fullData = Convert.FromBase64String(cipherText);

                if (fullData.Length < IV_SIZE + TAG_SIZE)
                {
                    throw new CryptographicException("Ciphertext is too short to be a valid encrypted save.");
                }


                // Extract iv and tag
                byte[] iv = new byte[IV_SIZE];
                byte[] tag = new byte[TAG_SIZE];
                Buffer.BlockCopy(fullData, 0, iv, 0, IV_SIZE);
                Buffer.BlockCopy(fullData, fullData.Length - TAG_SIZE, tag, 0, TAG_SIZE);

                // Verify HMAC over (iv || ciphertext) BEFORE decrypting — fail fast on tamper
                int cipherLength = fullData.Length - IV_SIZE - TAG_SIZE;
                byte[] ivAndCipher = new byte[IV_SIZE + cipherLength];
                Buffer.BlockCopy(fullData, 0, ivAndCipher, 0, IV_SIZE + cipherLength);
                byte[] expectedTag = ComputeHmac(ivAndCipher, _material.MacKey);

                if (!ConstantTimeEquals(tag, expectedTag))
                {
                    throw new CryptographicException("Authentication failed. The save file has been tampered with, or the master key is incorrect.");
                }

                // HMAC verified — safe to decrypt
                using Aes aes = Aes.Create();
                aes.Key = _material.EncKey;
                aes.IV = iv;

                using ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, iv);
                using MemoryStream msDecrypt = new(fullData, IV_SIZE, cipherLength);
                using CryptoStream csDecrypt = new(msDecrypt, decryptor, CryptoStreamMode.Read);
                using StreamReader srDecrypt = new(csDecrypt);

                return srDecrypt.ReadToEnd();
            }
            catch (CryptographicException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new CryptographicException($"Unknown error during decryption: {e.Message}", e);
            }
        }

        #region Helper
        /// <summary>
        /// Derives a pair of independent 256-bit keys from the master key string.
        /// Uses HMAC-SHA256 with distinct labels as a poor-man's KDF (HKDF not available on Mono).
        /// </summary>
        private static KeyMaterial DeriveKeyMaterial(string masterKey)
        {
            byte[] masterBytes = Encoding.UTF8.GetBytes(masterKey);
            using HMACSHA256 hmac = new(masterBytes);
            byte[] enc = hmac.ComputeHash(Encoding.UTF8.GetBytes("ENCKey"));
            byte[] mac = hmac.ComputeHash(Encoding.UTF8.GetBytes("MACKey"));

            return new KeyMaterial(enc, mac);
        }

        /// <summary>
        /// Computes HMAC-SHA256 over the given data with the given key.
        /// </summary>
        private static byte[] ComputeHmac(byte[] data, byte[] key)
        {
            using HMACSHA256 hmac = new(key);
            return hmac.ComputeHash(data);
        }

        /// <summary>
        /// Constant-time byte array comparison to prevent timing side-channel attacks on HMAC verification.
        /// </summary>
        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }

            return diff == 0;
        }

        /// <summary>
        /// Pair of derived keys used per save: one for AES-CBC, one for HMAC-SHA256.
        /// Both 32 bytes (256-bit) — derived from the master key via HMAC-based KDF.
        /// </summary>
        private readonly struct KeyMaterial
        {
            public readonly byte[] EncKey;
            public readonly byte[] MacKey;

            public KeyMaterial(byte[] enc, byte[] mac)
            {
                EncKey = enc;
                MacKey = mac;
            }
        }
        #endregion
    }
}