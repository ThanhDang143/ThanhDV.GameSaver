using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ThanhDV.GameSaver.CustomAttribute
{
    /// <summary>
    /// Versioned encryption/decryption utility with PBKDF2 key derivation.
    /// New payloads are encoded as Base64 with the layout:
    /// [1 byte version][16 byte salt][16 byte IV][32 byte HMAC][ciphertext].
    /// Legacy payloads remain decryptable to preserve backward compatibility.
    /// </summary>
    public static class AESProvider
    {
        #region Encrypt/Decrypt
        private const int SaltSize = 16;               // 128-bit salt
        private const int NonceSize = 12;              // 96-bit nonce for GCM
        private const int IvSize = 16;                 // 128-bit IV for CBC
        private const int KeySize = 32;                // 256-bit AES key (GCM or encKey part)
        private const int TagSize = 16;                // 128-bit GCM tag
        private const int HmacSize = 32;               // HMAC-SHA256 size
        private const int LegacyPBKDF2Iterations = 14398;
        private const int CurrentPBKDF2Iterations = 100000;

        private const byte VERSION_GCM = 1;
        private const byte VERSION_CBC_HMAC_LEGACY = 2;
        private const byte VERSION_CBC_HMAC = 3;

        /// <summary>
        /// Encrypt UTF-8 text using the stable AES-CBC+HMAC format.
        /// </summary>
        /// <param name="plaintext">Plain text (UTF-8) to encrypt.</param>
        /// <param name="passphrase">Passphrase used to derive symmetric key(s).</param>
        /// <param name="associatedData">Optional AAD (authenticated but not encrypted).</param>
        /// <returns>Base64 package (see class summary for layout).</returns>
        public static string Encrypt(string plaintext, string passphrase, byte[] associatedData = null)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase required", nameof(passphrase));

            byte[] salt = RandomBytes(SaltSize);
            return EncryptCBCHMAC(plaintext, passphrase, salt, associatedData);
        }

        /// <summary>
        /// Decrypt Base64 package produced by <see cref="Encrypt"/>.
        /// </summary>
        /// <param name="encryptedBase64">Base64 package.</param>
        /// <param name="passphrase">Passphrase used at encryption time.</param>
        /// <param name="associatedData">AAD used at encryption (must match).</param>
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
                VERSION_CBC_HMAC_LEGACY => DecryptCBCHMACLegacy(data, passphrase, associatedData),
                VERSION_CBC_HMAC => DecryptCBCHMAC(data, passphrase, associatedData),
                _ => throw new CryptographicException($"Unsupported version: {version}")
            };
        }
        #endregion

        #region GCM
        private static string EncryptGCM(string plaintext, string passphrase, byte[] salt, byte[] associatedData)
        {
            byte[] key = DeriveKey(passphrase, salt, KeySize, LegacyPBKDF2Iterations); // 32 bytes
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

            byte[] key = DeriveKey(passphrase, salt, KeySize, LegacyPBKDF2Iterations);
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
            byte[] keyMaterial = DeriveKey(passphrase, salt, KeySize * 2, CurrentPBKDF2Iterations); // 64 bytes
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

            byte[] hmacInput = BuildMacInput(VERSION_CBC_HMAC, salt, iv, cipher, associatedData);
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

            byte[] keyMaterial = DeriveKey(passphrase, salt, KeySize * 2, CurrentPBKDF2Iterations);
            byte[] encKey = new byte[KeySize];
            byte[] authKey = new byte[KeySize];
            Buffer.BlockCopy(keyMaterial, 0, encKey, 0, KeySize);
            Buffer.BlockCopy(keyMaterial, KeySize, authKey, 0, KeySize);

            byte[] hmacInput = BuildMacInput(VERSION_CBC_HMAC, salt, iv, cipher, associatedData);
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

        private static string DecryptCBCHMACLegacy(byte[] input, string passphrase, byte[] associatedData)
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

            byte[] keyMaterial = DeriveKey(passphrase, salt, KeySize * 2, LegacyPBKDF2Iterations);
            byte[] encKey = new byte[KeySize];
            byte[] authKey = new byte[KeySize];
            Buffer.BlockCopy(keyMaterial, 0, encKey, 0, KeySize);
            Buffer.BlockCopy(keyMaterial, KeySize, authKey, 0, KeySize);

            byte[] hmacInput = BuildLegacyMacInput(iv, cipher, associatedData);
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

        private static byte[] BuildLegacyMacInput(byte[] iv, byte[] cipher, byte[] aad)
        {
            aad ??= Array.Empty<byte>();
            byte[] lengthBytes = BitConverter.GetBytes(aad.Length);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBytes);
            }

            byte[] result = new byte[iv.Length + cipher.Length + lengthBytes.Length + aad.Length];
            int o = 0;
            Buffer.BlockCopy(iv, 0, result, o, iv.Length); o += iv.Length;
            Buffer.BlockCopy(cipher, 0, result, o, cipher.Length); o += cipher.Length;
            Buffer.BlockCopy(lengthBytes, 0, result, o, lengthBytes.Length); o += lengthBytes.Length;
            Buffer.BlockCopy(aad, 0, result, o, aad.Length);
            return result;
        }

        private static byte[] BuildMacInput(byte version, byte[] salt, byte[] iv, byte[] cipher, byte[] aad)
        {
            aad ??= Array.Empty<byte>();
            byte[] aadLength = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(aadLength, aad.Length);

            byte[] result = new byte[1 + salt.Length + iv.Length + cipher.Length + aadLength.Length + aad.Length];
            int o = 0;
            result[o++] = version;
            Buffer.BlockCopy(salt, 0, result, o, salt.Length); o += salt.Length;
            Buffer.BlockCopy(iv, 0, result, o, iv.Length); o += iv.Length;
            Buffer.BlockCopy(cipher, 0, result, o, cipher.Length); o += cipher.Length;
            Buffer.BlockCopy(aadLength, 0, result, o, aadLength.Length); o += aadLength.Length;
            Buffer.BlockCopy(aad, 0, result, o, aad.Length);
            return result;
        }
        #endregion

        #region Key Derivation Helpers
        /// <summary>
        /// Derive 'size' bytes of key material via PBKDF2(SHA256, iterations).
        /// </summary>
        private static byte[] DeriveKey(string passphrase, byte[] salt, int size, int iterations)
        {
            using var kdf = new Rfc2898DeriveBytes(passphrase, salt, iterations, HashAlgorithmName.SHA256);
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
        private const string PassphraseDomain = "GameSaver|ThanhDV|Passphrase|v2";
        private static ISecretStorage secretStorage;

        public interface ISecretStorage
        {
            string LoadSecret();
            void SaveSecret(string secret);
            void DeleteSecret();
        }

        private sealed class PlayerPrefsSecretStorage : ISecretStorage
        {
            public string LoadSecret() => PlayerPrefs.GetString(PREF_SECRET_KEY, null);
            public void SaveSecret(string secret)
            {
                PlayerPrefs.SetString(PREF_SECRET_KEY, secret);
                PlayerPrefs.Save();
            }

            public void DeleteSecret()
            {
                if (!PlayerPrefs.HasKey(PREF_SECRET_KEY))
                {
                    return;
                }

                PlayerPrefs.DeleteKey(PREF_SECRET_KEY);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Configures secret persistence for GetPassphrase. Inject a platform keystore-backed implementation
        /// if you need stronger protection than the PlayerPrefs fallback.
        /// </summary>
        public static void SetSecretStorage(ISecretStorage storage)
        {
            secretStorage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        /// <summary>
        /// Returns a deterministic Base64 passphrase derived from a stored random secret.
        /// Passphrases are device-independent by default to keep save data portable across platforms.
        /// </summary>
        /// <param name="bindDevice">Legacy option that binds the derived passphrase to the current device id.</param>
        /// <returns>Base64 SHA-256(localSecret|domain|optional device id).</returns>
        /// <remarks>Call ResetPassphrase to force a new secret/passphrase.</remarks>
        public static string GetPassphrase(bool bindDevice = false)
        {
            ISecretStorage storage = secretStorage ??= new PlayerPrefsSecretStorage();
            string localSecret = storage.LoadSecret();
            if (string.IsNullOrEmpty(localSecret))
            {
                byte[] rnd = RandomBytes(32);
                localSecret = Convert.ToBase64String(rnd);
                storage.SaveSecret(localSecret);
            }

            string deviceComponent = bindDevice ? (SystemInfo.deviceUniqueIdentifier ?? "no_device") : "cross_platform";

            using var sha = SHA256.Create();
            byte[] mix = Encoding.UTF8.GetBytes(localSecret + "|" + PassphraseDomain + "|" + deviceComponent);
            byte[] hash = sha.ComputeHash(mix);
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Deletes the stored secret so next GetPassphrase generates a new one.
        /// </summary>
        public static void ResetPassphrase()
        {
            (secretStorage ??= new PlayerPrefsSecretStorage()).DeleteSecret();
        }
        #endregion
    }
}
