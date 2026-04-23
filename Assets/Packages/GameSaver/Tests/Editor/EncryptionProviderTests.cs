using System;
using System.Security.Cryptography;
using NUnit.Framework;
using ThanhDV.GameSaver.Infrastructure;

namespace ThanhDV.GameSaver.Tests.Editor
{
    public class EncryptionProviderTests
    {
        private const string ValidSecretKey = "MySuperSecretKey123!@#";
        private AESProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _provider = new AESProvider(ValidSecretKey);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Constructor_NullOrEmptyKey_ThrowsArgumentNullException(string invalidKey)
        {
            Assert.Throws<ArgumentNullException>(() => new AESProvider(invalidKey));
        }

        [Test]
        public void Encrypt_ValidString_ReturnsEncryptedString()
        {
            const string plainText = "Hello World! This is a test.";
            string encrypted = _provider.Encrypt(plainText);

            Assert.IsNotEmpty(encrypted);
            Assert.AreNotEqual(plainText, encrypted);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Encrypt_NullOrEmptyString_ReturnsSame(string input)
        {
            string encrypted = _provider.Encrypt(input);
            Assert.AreEqual(input, encrypted);
        }

        [Test]
        public void Decrypt_ValidEncryptedString_ReturnsOriginalString()
        {
            const string plainText = "The quick brown fox jumps over the lazy dog.";
            string encrypted = _provider.Encrypt(plainText);
            string decrypted = _provider.Decrypt(encrypted);

            Assert.AreEqual(plainText, decrypted);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Decrypt_NullOrEmptyString_ReturnsSame(string input)
        {
            string decrypted = _provider.Decrypt(input);
            Assert.AreEqual(input, decrypted);
        }

        [Test]
        public void Decrypt_WithWrongKey_ThrowsCryptographicException()
        {
            const string plainText = "Defend the east wall!";
            string encrypted = _provider.Encrypt(plainText);

            var wrongProvider = new AESProvider("WrongKey456");

            Assert.Throws<CryptographicException>(() => wrongProvider.Decrypt(encrypted));
        }

        [Test]
        public void Decrypt_TamperedData_ThrowsCryptographicException()
        {
            const string plainText = "Important save data";
            string encrypted = _provider.Encrypt(plainText);

            // Tamper with the base64 string
            string tampered = encrypted.Substring(0, encrypted.Length - 5) + "ABCDE";

            Assert.Throws<CryptographicException>(() => _provider.Decrypt(tampered));
        }

        [Test]
        public void Decrypt_InvalidBase64_ThrowsCryptographicException()
        {
            const string invalidBase64 = "ThisIsNotValidBase64**==";

            Assert.Throws<CryptographicException>(() => _provider.Decrypt(invalidBase64));
        }
    }
}
