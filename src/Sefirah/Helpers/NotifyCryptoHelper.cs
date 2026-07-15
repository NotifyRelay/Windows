using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NotifyRelay.Helpers;

public static class NotifyCryptoHelper
{
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
        username = Regex.Replace(username, "[^a-zA-Z0-9]", string.Empty)
            .ToLowerInvariant();
        username = usernamePrefix + username[..Math.Min(16, username.Length)];

        var passwordBytes = derived.Take(passwordLength).ToArray();
        var password = Convert.ToBase64String(passwordBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
        password = Regex.Replace(password, "[^a-zA-Z0-9]", string.Empty);

        return (username, password);
    }

    public static string DerivePasswordHash(string password)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hash);
    }

    public static string GenerateRandomPassword()
    {
        const string allowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                                    "abcdefghijklmnopqrstuvwxyz" +
                                    "0123456789" +
                                    "!@#$%^&*";

        return new string(Enumerable.Range(1, 12)
            .Select(_ => allowedChars[Random.Shared.Next(allowedChars.Length)])
            .ToArray());
    }
}
