using System;
using System.Security.Cryptography;
using NUnit.Framework;
using ThanhDV.GameSaver.Infrastructure;

namespace ThanhDV.GameSaver.Tests.Editor
{
    public class EncryptionProviderTests
    {
        // File format constants — kept private inside AESProvider, mirrored here for tamper-target tests.
        private const int IV_SIZE = 16;
        private const int TAG_SIZE = 32;
        private const int MIN_VALID_PAYLOAD = IV_SIZE + TAG_SIZE;

        private const string ValidSecretKey = "MySuperSecretKey123!@#";
        private const string OtherSecretKey = "AnotherDifferentKey789$%^";

        private AESProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _provider = new AESProvider(ValidSecretKey);
        }

        #region Constructor validation

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("   ")]
        [TestCase("\t")]
        public void Constructor_NullEmptyOrWhitespaceKey_ThrowsArgumentException(string invalidKey)
        {
            Assert.Throws<ArgumentException>(() => new AESProvider(invalidKey));
        }

        [Test]
        public void Constructor_ValidKey_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => new AESProvider(ValidSecretKey));
        }

        #endregion

        #region Encrypt — basic behavior

        [Test]
        public void Encrypt_ValidString_ReturnsNonEmptyCipher()
        {
            const string plainText = "Hello World! This is a test.";
            string encrypted = _provider.Encrypt(plainText);

            Assert.IsNotEmpty(encrypted);
            Assert.AreNotEqual(plainText, encrypted, "Cipher output should not equal the input plaintext.");
        }

        [TestCase(null)]
        [TestCase("")]
        public void Encrypt_NullOrEmpty_ReturnsSame(string input)
        {
            Assert.AreEqual(input, _provider.Encrypt(input));
        }

        [Test]
        public void Encrypt_SamePlaintextTwice_ProducesDifferentCiphertexts()
        {
            // Different random IV per call → different ciphertexts. Critical for CBC semantic security.
            const string plainText = "Repeated content";
            string c1 = _provider.Encrypt(plainText);
            string c2 = _provider.Encrypt(plainText);

            Assert.AreNotEqual(c1, c2, "Each Encrypt call must use a fresh random IV — identical outputs leak that two saves had the same content.");
        }

        [Test]
        public void Encrypt_OutputLength_IsAtLeastIVPlusTag()
        {
            // Output (decoded) must contain at minimum 16-byte IV + 32-byte tag, even for empty-ish plaintext.
            string encrypted = _provider.Encrypt("x");
            byte[] decoded = Convert.FromBase64String(encrypted);

            Assert.That(decoded.Length, Is.GreaterThanOrEqualTo(MIN_VALID_PAYLOAD));
        }

        #endregion

        #region Decrypt — basic behavior

        [TestCase(null)]
        [TestCase("")]
        public void Decrypt_NullOrEmpty_ReturnsSame(string input)
        {
            Assert.AreEqual(input, _provider.Decrypt(input));
        }

        [Test]
        public void Decrypt_ValidCipher_ReturnsOriginalPlaintext()
        {
            const string plainText = "The quick brown fox jumps over the lazy dog.";
            string encrypted = _provider.Encrypt(plainText);
            string decrypted = _provider.Decrypt(encrypted);

            Assert.AreEqual(plainText, decrypted);
        }

        [Test]
        public void RoundTrip_UnicodeText_PreservesContent()
        {
            const string plainText = "Xin chào — Hello — 你好 — 🎮💾";
            string encrypted = _provider.Encrypt(plainText);
            string decrypted = _provider.Decrypt(encrypted);

            Assert.AreEqual(plainText, decrypted);
        }

        [Test]
        public void RoundTrip_LargeText_PreservesContent()
        {
            // 50KB of data — exercises stream-based encrypt/decrypt
            string plainText = new('A', 50_000);
            string encrypted = _provider.Encrypt(plainText);
            string decrypted = _provider.Decrypt(encrypted);

            Assert.AreEqual(plainText, decrypted);
        }

        #endregion

        #region Decrypt — bad-key and tampering rejection (HMAC integrity)

        [Test]
        public void Decrypt_WithWrongKey_ThrowsCryptographicException()
        {
            string encrypted = _provider.Encrypt("Secret payload");
            AESProvider wrongProvider = new(OtherSecretKey);

            Assert.Throws<CryptographicException>(() => wrongProvider.Decrypt(encrypted),
                "Decrypt with a different master key must fail at HMAC verification.");
        }

        [Test]
        public void Decrypt_TamperedIV_ThrowsCryptographicException()
        {
            // Flip a bit inside the IV region (bytes 0..15). HMAC covers IV → must detect.
            string encrypted = _provider.Encrypt("payload");
            string tampered = FlipBitAt(encrypted, byteIndex: 5);

            Assert.Throws<CryptographicException>(() => _provider.Decrypt(tampered));
        }

        [Test]
        public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
        {
            // Flip a bit inside the ciphertext region (after IV, before tag). HMAC must catch.
            string encrypted = _provider.Encrypt("payload that is long enough");
            byte[] bytes = Convert.FromBase64String(encrypted);
            int ciphertextMid = IV_SIZE + (bytes.Length - IV_SIZE - TAG_SIZE) / 2;
            string tampered = FlipBitAt(encrypted, byteIndex: ciphertextMid);

            Assert.Throws<CryptographicException>(() => _provider.Decrypt(tampered));
        }

        [Test]
        public void Decrypt_TamperedTag_ThrowsCryptographicException()
        {
            // Flip a bit inside the tag region (last 32 bytes). Direct tag mismatch.
            string encrypted = _provider.Encrypt("payload");
            byte[] bytes = Convert.FromBase64String(encrypted);
            int tagByte = bytes.Length - 5;
            string tampered = FlipBitAt(encrypted, byteIndex: tagByte);

            Assert.Throws<CryptographicException>(() => _provider.Decrypt(tampered));
        }

        [Test]
        public void Decrypt_TruncatedToBelowMinSize_ThrowsCryptographicException()
        {
            // Anything shorter than IV+TAG cannot possibly be a valid encrypted save.
            string tooShort = Convert.ToBase64String(new byte[MIN_VALID_PAYLOAD - 1]);

            Assert.Throws<CryptographicException>(() => _provider.Decrypt(tooShort));
        }

        [Test]
        public void Decrypt_InvalidBase64_ThrowsCryptographicException()
        {
            const string invalidBase64 = "ThisIsNotValidBase64**==";
            Assert.Throws<CryptographicException>(() => _provider.Decrypt(invalidBase64));
        }

        [Test]
        public void Decrypt_TruncatedCiphertext_ThrowsCryptographicException()
        {
            // Cut off the last few bytes (some of the tag missing) — HMAC verification must still reject.
            string encrypted = _provider.Encrypt("payload");
            byte[] bytes = Convert.FromBase64String(encrypted);
            byte[] truncated = new byte[bytes.Length - 5];
            Array.Copy(bytes, truncated, truncated.Length);
            string tampered = Convert.ToBase64String(truncated);

            Assert.Throws<CryptographicException>(() => _provider.Decrypt(tampered));
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Decodes a Base64 string, flips one bit at the given byte index, and re-encodes.
        /// Used to simulate tampering at specific positions in the file format.
        /// </summary>
        private static string FlipBitAt(string base64, int byteIndex)
        {
            byte[] bytes = Convert.FromBase64String(base64);
            bytes[byteIndex] ^= 0x01;
            return Convert.ToBase64String(bytes);
        }

        #endregion
    }
}
