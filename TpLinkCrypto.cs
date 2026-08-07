using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace NetPulseMonitor;

internal sealed class TpLinkCrypto
{
    private const string RandomCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private readonly byte[] _key;
    private readonly byte[] _iv;
    private readonly string _keyText;
    private readonly string _ivText;
    private readonly string _hash;
    private readonly string _modulusHex;
    private readonly string _exponentHex;
    private readonly long _sequence;

    public TpLinkCrypto(
        string userName,
        string password,
        string modulusHex,
        string exponentHex,
        long sequence)
    {
        _keyText = CreateRandomAscii(16);
        _ivText = CreateRandomAscii(16);
        _key = Encoding.UTF8.GetBytes(_keyText);
        _iv = Encoding.UTF8.GetBytes(_ivText);
        _hash = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(userName + password)))
            .ToLowerInvariant();
        _modulusHex = modulusHex;
        _exponentHex = exponentHex;
        _sequence = sequence;
    }

    public EncryptedPayload EncryptLogin(string userName, string password)
    {
        string data = EncryptAes(userName + "\n" + password);
        string signatureText =
            $"key={_keyText}&iv={_ivText}&h={_hash}&s={_sequence + data.Length}";
        return new EncryptedPayload(data,
            EncryptRsaWithoutPadding(signatureText, _modulusHex, _exponentHex));
    }

    public EncryptedPayload EncryptRequest(string plainText)
    {
        string data = EncryptAes(plainText);
        string signatureText = $"h={_hash}&s={_sequence + data.Length}";
        return new EncryptedPayload(data,
            EncryptRsaWithoutPadding(signatureText, _modulusHex, _exponentHex));
    }

    public string DecryptResponse(string encryptedBase64)
    {
        byte[] encrypted = Convert.FromBase64String(encryptedBase64.Trim());
        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = _key;
        aes.IV = _iv;
        using ICryptoTransform decryptor = aes.CreateDecryptor();
        byte[] plain = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            Array.Clear(plain);
            Array.Clear(encrypted);
        }
    }

    private string EncryptAes(string plainText)
    {
        byte[] plain = Encoding.UTF8.GetBytes(plainText);
        using Aes aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = _key;
        aes.IV = _iv;
        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] encrypted = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        try
        {
            return Convert.ToBase64String(encrypted);
        }
        finally
        {
            Array.Clear(plain);
            Array.Clear(encrypted);
        }
    }

    private static string EncryptRsaWithoutPadding(
        string value,
        string modulusHex,
        string exponentHex)
    {
        byte[] modulusBytes = Convert.FromHexString(NormalizeHex(modulusHex));
        byte[] exponentBytes = Convert.FromHexString(NormalizeHex(exponentHex));
        BigInteger modulus = new(modulusBytes, isUnsigned: true, isBigEndian: true);
        BigInteger exponent = new(exponentBytes, isUnsigned: true, isBigEndian: true);
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);
        int blockSize = modulusBytes.Length;
        var result = new StringBuilder();

        try
        {
            for (int offset = 0; offset < valueBytes.Length; offset += blockSize)
            {
                int length = Math.Min(blockSize, valueBytes.Length - offset);
                byte[] block = new byte[blockSize];
                Buffer.BlockCopy(valueBytes, offset, block, 0, length);
                BigInteger message = new(block, isUnsigned: true, isBigEndian: true);
                BigInteger cipher = BigInteger.ModPow(message, exponent, modulus);
                byte[] cipherBytes = cipher.ToByteArray(isUnsigned: true, isBigEndian: true);
                string hex = Convert.ToHexString(cipherBytes).ToLowerInvariant();
                result.Append(hex.PadLeft(blockSize * 2, '0'));
                Array.Clear(block);
                Array.Clear(cipherBytes);
            }
        }
        finally
        {
            Array.Clear(valueBytes);
            Array.Clear(modulusBytes);
            Array.Clear(exponentBytes);
        }

        return result.ToString();
    }

    private static string NormalizeHex(string value)
    {
        string hex = value.Trim();
        return hex.Length % 2 == 0 ? hex : "0" + hex;
    }

    private static string CreateRandomAscii(int length)
    {
        var result = new char[length];
        for (int index = 0; index < result.Length; index++)
            result[index] = RandomCharacters[
                RandomNumberGenerator.GetInt32(RandomCharacters.Length)];
        return new string(result);
    }
}

internal readonly record struct EncryptedPayload(string Data, string Signature);
