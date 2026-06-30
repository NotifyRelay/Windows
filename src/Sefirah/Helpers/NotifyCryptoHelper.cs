using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NotifyRelay.Helpers;

/// <summary>
/// Notify 协议加解密与密钥派生工具。
/// </summary>
public static class NotifyCryptoHelper
{
    /// <summary>
    /// 使用 ECDH 协商派生共享密钥。
    /// </summary>
    public static byte[] GenerateSharedSecretSmart(
        string localKey, byte[]? localPrivateKey, string remoteKey)
    {
        if (localPrivateKey == null || localPrivateKey.Length == 0)
            throw new InvalidOperationException("ECDH 模式需要私钥");
        return EcdhHelper.DeriveSharedSecretFromPrivate(localPrivateKey, remoteKey);
    }

    public static string Encrypt(string plainText, byte[] sharedSecret)
    {
        try
        {
            byte[] keyBytes = sharedSecret;
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plainText);

            byte[] iv = new byte[12];
            RandomNumberGenerator.Fill(iv);

            byte[] cipherText = new byte[plaintextBytes.Length];
            byte[] tag = new byte[16];

            using (var aesgcm = new AesGcm(keyBytes, 16))
            {
                aesgcm.Encrypt(iv, plaintextBytes, cipherText, tag, null);
            }

            byte[] output = new byte[iv.Length + cipherText.Length + tag.Length];
            Buffer.BlockCopy(iv, 0, output, 0, iv.Length);
            Buffer.BlockCopy(cipherText, 0, output, iv.Length, cipherText.Length);
            Buffer.BlockCopy(tag, 0, output, iv.Length + cipherText.Length, tag.Length);

            return Convert.ToBase64String(output);
        }
        catch
        {
            return plainText;
        }
    }

    public static string Decrypt(string encryptedText, byte[] sharedSecret)
    {
        try
        {
            byte[] keyBytes = sharedSecret;
            byte[] buffer = Convert.FromBase64String(encryptedText);

            if (buffer.Length < 28)
            {
                throw new ArgumentException("Invalid encrypted payload length");
            }

            byte[] iv = new byte[12];
            Buffer.BlockCopy(buffer, 0, iv, 0, iv.Length);

            int cipherLen = buffer.Length - iv.Length - 16;
            byte[] cipherText = new byte[cipherLen];
            Buffer.BlockCopy(buffer, iv.Length, cipherText, 0, cipherLen);

            byte[] tag = new byte[16];
            Buffer.BlockCopy(buffer, iv.Length + cipherLen, tag, 0, tag.Length);

            byte[] plainBytes = new byte[cipherLen];
            using (var aesgcm = new AesGcm(keyBytes, 16))
            {
                aesgcm.Decrypt(iv, cipherText, tag, plainBytes, null);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return encryptedText;
        }
    }



    public static (string Username, string Password) DeriveftpCredentials(byte[] sharedSecret)
    {
        const string usernamePrefix = "ftp_";
        const int passwordLength = 32;

        using var sha256 = SHA256.Create();
        var derived = sha256.ComputeHash(sharedSecret);

        var usernameBytes = derived.Take(8).ToArray();
        var username = Convert.ToBase64String(usernameBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
        // 移除所有非字母数字字符，与Android端保持一致
        username = System.Text.RegularExpressions.Regex.Replace(username, "[^a-zA-Z0-9]", string.Empty)
            .ToLowerInvariant();
        username = usernamePrefix + username[..Math.Min(16, username.Length)];

        var passwordBytes = derived.Take(passwordLength).ToArray();
        var password = Convert.ToBase64String(passwordBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
        // 移除所有非字母数字字符，与Android端保持一致
        password = System.Text.RegularExpressions.Regex.Replace(password, "[^a-zA-Z0-9]", string.Empty);

        return (username, password);
    }

    public static string DerivePasswordHash(string password)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hash);
    }
}