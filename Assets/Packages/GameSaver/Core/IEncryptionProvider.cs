namespace ThanhDV.GameSaver.Core
{
    public interface IEncryptionProvider
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }
}
