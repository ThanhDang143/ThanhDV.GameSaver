using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ThanhDV.GameSaver.DataHandler
{
    /// <summary>
    /// AES-GCM (AEAD) encryption/decryption utility with PBKDF2 key derivation.
    /// Output Base64 layout: [1 byte version][16 byte salt][12 byte nonce][16 byte tag][ciphertext].
    /// A fresh salt + nonce are generated each encryption to prevent reuse. Supply <paramref name="associatedData"/> if you need to authenticate (but not encrypt) extra context (e.g. file name, version).
    /// </summary>
    public static class AES
    {
        #region Encrypt/Decrypt
        private const int SaltSize = 16;               // 128-bit salt
        private const int NonceSize = 12;              // 96-bit nonce for GCM
        private const int IvSize = 16;                 // 128-bit IV for CBC
        private const int KeySize = 32;                // 256-bit AES key (GCM or encKey part)
        private const int TagSize = 16;                // 128-bit GCM tag
        private const int HmacSize = 32;               // HMAC-SHA256 size
        private const int PBKDF2_Iterations = 14398; // Adjust if performance allows

        private const byte VERSION_GCM = 1;
        private const byte VERSION_CBC_HMAC = 2;

        /// <summary>
        /// Encrypt UTF-8 text using AES-GCM (if supported) or fallback AES-CBC+HMAC.
        /// </summary>
        /// <param name="plaintext">Plain text (UTF-8) to encrypt.</param>
        /// <param name="passphrase">Passphrase used to derive symmetric key(s).</param>
        /// <param name="associatedData">Optional AAD (authenticated but not encrypted) - used only for GCM; included in HMAC calculation for CBC fallback.</param>
        /// <returns>Base64 package (see class summary for layout).</returns>
        public static string Encrypt(string plaintext, string passphrase, byte[] associatedData = null)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase required", nameof(passphrase));

            // Generate salt (unique per encryption)
            byte[] salt = RandomBytes(SaltSize);

            // Try GCM first
            try
            {
                return EncryptGCM(plaintext, passphrase, salt, associatedData);
            }
            catch (PlatformNotSupportedException)
            {
                // Fallback to CBC+HMAC
                return EncryptCBCHMAC(plaintext, passphrase, salt, associatedData);
            }
        }

        /// <summary>
        /// Decrypt Base64 package produced by <see cref="Encrypt"/>.
        /// </summary>
        /// <param name="encryptedBase64">Base64 package.</param>
        /// <param name="passphrase">Passphrase used at encryption time.</param>
        /// <param name="associatedData">AAD used at encryption (must match). For CBC fallback only used in HMAC.</param>
        /// <returns>Decrypted UTF-8 text.</returns>
        public static string Decrypt(string encryptedBase64, string passphrase, byte[] associatedData = null)
        {
            if (encryptedBase64 == null) throw new ArgumentNullException(nameof(encryptedBase64));
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase required", nameof(passphrase));

            byte[] data;
            try
            {
                data = Convert.FromBase64String(encryptedBase64);
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("Invalid Base64 input", ex);
            }
            if (data.Length < 1 + SaltSize)
                throw new CryptographicException("Cipher text too short");

            byte version = data[0];
            return version switch
            {
                VERSION_GCM => DecryptGCM(data, passphrase, associatedData),
                VERSION_CBC_HMAC => DecryptCBCHMAC(data, passphrase, associatedData),
                _ => throw new CryptographicException($"Unsupported version: {version}")
            };
        }
        #endregion

        #region GCM
        private static string EncryptGCM(string plaintext, string passphrase, byte[] salt, byte[] associatedData)
        {
            byte[] key = DeriveKey(passphrase, salt, KeySize); // 32 bytes
            byte[] nonce = RandomBytes(NonceSize);
            byte[] plain = Encoding.UTF8.GetBytes(plaintext);
            byte[] cipher = new byte[plain.Length];
            byte[] tag = new byte[TagSize];

            using (var gcm = new AesGcm(key))
            {
                gcm.Encrypt(nonce, plain, cipher, tag, associatedData);
            }

            byte[] output = new byte[1 + SaltSize + NonceSize + TagSize + cipher.Length];
            int o = 0;
            output[o++] = VERSION_GCM;
            Buffer.BlockCopy(salt, 0, output, o, SaltSize); o += SaltSize;
            Buffer.BlockCopy(nonce, 0, output, o, NonceSize); o += NonceSize;
            Buffer.BlockCopy(tag, 0, output, o, TagSize); o += TagSize;
            Buffer.BlockCopy(cipher, 0, output, o, cipher.Length);

            return Convert.ToBase64String(output);
        }

        private static string DecryptGCM(byte[] input, string passphrase, byte[] associatedData)
        {
            int min = 1 + SaltSize + NonceSize + TagSize;
            if (input.Length < min) throw new CryptographicException("GCM cipher text too short");

            int o = 1;
            byte[] salt = new byte[SaltSize]; Buffer.BlockCopy(input, o, salt, 0, SaltSize); o += SaltSize;
            byte[] nonce = new byte[NonceSize]; Buffer.BlockCopy(input, o, nonce, 0, NonceSize); o += NonceSize;
            byte[] tag = new byte[TagSize]; Buffer.BlockCopy(input, o, tag, 0, TagSize); o += TagSize;
            int cipherLen = input.Length - o;
            if (cipherLen < 0) throw new CryptographicException("Invalid GCM layout");
            byte[] cipher = new byte[cipherLen]; Buffer.BlockCopy(input, o, cipher, 0, cipherLen);

            byte[] key = DeriveKey(passphrase, salt, KeySize);
            byte[] plain = new byte[cipher.Length];

            try
            {
                using var gcm = new AesGcm(key);
                gcm.Decrypt(nonce, cipher, tag, plain, associatedData);
            }
            catch (PlatformNotSupportedException)
            {
                throw new CryptographicException("AesGcm not supported on this platform (cannot decrypt GCM data)");
            }
            catch (CryptographicException)
            {
                throw new CryptographicException("Failed to decrypt (wrong passphrase/AAD or tampered data)");
            }

            return Encoding.UTF8.GetString(plain);
        }
        #endregion

        #region CBC + HMAC
        private static string EncryptCBCHMAC(string plaintext, string passphrase, byte[] salt, byte[] associatedData)
        {
            byte[] keyMaterial = DeriveKey(passphrase, salt, KeySize * 2); // 64 bytes
            byte[] encKey = new byte[KeySize];
            byte[] authKey = new byte[KeySize];
            Buffer.BlockCopy(keyMaterial, 0, encKey, 0, KeySize);
            Buffer.BlockCopy(keyMaterial, KeySize, authKey, 0, KeySize);

            byte[] iv = RandomBytes(IvSize);
            byte[] plain = Encoding.UTF8.GetBytes(plaintext);
            byte[] cipher;

            using (var aes = Aes.Create())
            {
                aes.Key = encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var enc = aes.CreateEncryptor();
                cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
            }

            // HMAC over: iv || cipher || (AAD length + AAD)  (AAD length 4 bytes, little endian)
            byte[] hmacInput = BuildHMACInput(iv, cipher, associatedData);
            byte[] hmac;
            using (var h = new HMACSHA256(authKey))
                hmac = h.ComputeHash(hmacInput);

            byte[] output = new byte[1 + SaltSize + IvSize + HmacSize + cipher.Length];
            int o = 0;
            output[o++] = VERSION_CBC_HMAC;
            Buffer.BlockCopy(salt, 0, output, o, SaltSize); o += SaltSize;
            Buffer.BlockCopy(iv, 0, output, o, IvSize); o += IvSize;
            Buffer.BlockCopy(hmac, 0, output, o, HmacSize); o += HmacSize;
            Buffer.BlockCopy(cipher, 0, output, o, cipher.Length);

            return Convert.ToBase64String(output);
        }

        private static string DecryptCBCHMAC(byte[] input, string passphrase, byte[] associatedData)
        {
            int min = 1 + SaltSize + IvSize + HmacSize;
            if (input.Length < min) throw new CryptographicException("CBC cipher text too short");

            int o = 1;
            byte[] salt = new byte[SaltSize]; Buffer.BlockCopy(input, o, salt, 0, SaltSize); o += SaltSize;
            byte[] iv = new byte[IvSize]; Buffer.BlockCopy(input, o, iv, 0, IvSize); o += IvSize;
            byte[] hmac = new byte[HmacSize]; Buffer.BlockCopy(input, o, hmac, 0, HmacSize); o += HmacSize;
            int cipherLen = input.Length - o;
            if (cipherLen < 0) throw new CryptographicException("Invalid CBC layout");
            byte[] cipher = new byte[cipherLen]; Buffer.BlockCopy(input, o, cipher, 0, cipherLen);

            byte[] keyMaterial = DeriveKey(passphrase, salt, KeySize * 2);
            byte[] encKey = new byte[KeySize];
            byte[] authKey = new byte[KeySize];
            Buffer.BlockCopy(keyMaterial, 0, encKey, 0, KeySize);
            Buffer.BlockCopy(keyMaterial, KeySize, authKey, 0, KeySize);

            byte[] hmacInput = BuildHMACInput(iv, cipher, associatedData);
            byte[] calc;
            using (var h = new HMACSHA256(authKey))
                calc = h.ComputeHash(hmacInput);

            if (!CryptographicOperations.FixedTimeEquals(hmac, calc))
                throw new CryptographicException("HMAC mismatch (tampered or wrong passphrase/AAD)");

            byte[] plain;
            try
            {
                using var aes = Aes.Create();
                aes.Key = encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var dec = aes.CreateDecryptor();
                plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("CBC decrypt failed", ex);
            }

            return Encoding.UTF8.GetString(plain);
        }

        private static byte[] BuildHMACInput(byte[] iv, byte[] cipher, byte[] aad)
        {
            aad ??= Array.Empty<byte>();
            byte[] lengthBytes = BitConverter.GetBytes(aad.Length); // little-endian
            byte[] result = new byte[iv.Length + cipher.Length + lengthBytes.Length + aad.Length];
            int o = 0;
            Buffer.BlockCopy(iv, 0, result, o, iv.Length); o += iv.Length;
            Buffer.BlockCopy(cipher, 0, result, o, cipher.Length); o += cipher.Length;
            Buffer.BlockCopy(lengthBytes, 0, result, o, lengthBytes.Length); o += lengthBytes.Length;
            Buffer.BlockCopy(aad, 0, result, o, aad.Length);
            return result;
        }
        #endregion

        #region Key Derivation Helpers
        /// <summary>
        /// Derive 'size' bytes of key material via PBKDF2(SHA256, iterations).
        /// </summary>
        private static byte[] DeriveKey(string passphrase, byte[] salt, int size)
        {
            using var kdf = new Rfc2898DeriveBytes(passphrase, salt, PBKDF2_Iterations, HashAlgorithmName.SHA256);
            return kdf.GetBytes(size);
        }

        private static byte[] RandomBytes(int size)
        {
            byte[] b = new byte[size];
            RandomNumberGenerator.Fill(b);
            return b;
        }
        #endregion

        #region Passphrase
        private const string PREF_SECRET_KEY = "GameSaver.LocalSecret";
        private const string CONST_SALT = "GameSaver|ThanhDV|Const";

        /// <summary>
        /// Returns deterministic Base64 passphrase from stored random secret, optional device id, and constant salt.
        /// Creates and stores the secret if missing.
        /// </summary>
        /// <param name="bindDevice">Include device id to bind passphrase to device.</param>
        /// <returns>Base64 SHA-256(localSecret|deviceId|constSalt).</returns>
        /// <remarks>Call ResetPassphrase to force a new secret/passphrase.</remarks>
        public static string GetPassphrase(bool bindDevice = true)
        {
            // Create secret if have no secret
            string localSecret = PlayerPrefs.GetString(PREF_SECRET_KEY, null);
            if (string.IsNullOrEmpty(localSecret))
            {
                byte[] rnd = RandomBytes(32);
                localSecret = Convert.ToBase64String(rnd);
                PlayerPrefs.SetString(PREF_SECRET_KEY, localSecret);
                PlayerPrefs.Save();
            }

            string deviceId = bindDevice ? (SystemInfo.deviceUniqueIdentifier ?? "no_device") : "any_device";

            // Combine and hash -> passphrase
            using var sha = SHA256.Create();
            byte[] mix = Encoding.UTF8.GetBytes(localSecret + "|" + deviceId + "|" + CONST_SALT);
            byte[] hash = sha.ComputeHash(mix);
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Deletes the stored secret so next GetPassphrase generates a new one.
        /// </summary>
        public static void ResetPassphrase()
        {
            if (PlayerPrefs.HasKey(PREF_SECRET_KEY))
            {
                PlayerPrefs.DeleteKey(PREF_SECRET_KEY);
                PlayerPrefs.Save();
            }
        }
        #endregion
    }
}
