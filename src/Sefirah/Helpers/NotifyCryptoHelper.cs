using System.Text;
using System.Text.Json;
using NotifyRelay.Native;

namespace NotifyRelay.Helpers;

/// <summary>
/// 密码学工具类。
/// 实际计算委托给 Rust Core (nrc_derive_ftp_credentials / nrc_derive_password_hash / nrc_generate_random_password)。
/// </summary>
public static class NotifyCryptoHelper
{
    public static (string Username, string Password) DeriveftpCredentials(byte[] sharedSecret)
    {
        var secretB64 = Convert.ToBase64String(sharedSecret);
        var jsonStr = NotifyRelayCore.Safe.DeriveFtpCredentials(secretB64);
        if (string.IsNullOrEmpty(jsonStr))
            return ("", "");

        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;
            var username = root.GetProperty("username").GetString() ?? "";
            var password = root.GetProperty("password").GetString() ?? "";
            return (username, password);
        }
        catch
        {
            return ("", "");
        }
    }

    public static string DerivePasswordHash(string password)
    {
        return NotifyRelayCore.Safe.DerivePasswordHash(password) ?? "";
    }

    public static string GenerateRandomPassword()
    {
        return NotifyRelayCore.Safe.GenerateRandomPassword() ?? "";
    }
}
