namespace NotifyRelay.Helpers;

public static class PairingCodeHelper
{
    private const int CodeLength = 6;
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private static string? _currentCode;
    private static DateTime _generatedAt;

    public static string GenerateCode()
    {
        var random = new Random();
        _currentCode = random.Next(100000, 999999).ToString();
        _generatedAt = DateTime.UtcNow;
        return _currentCode;
    }

    public static string? GetCurrentCode()
    {
        if (_currentCode == null) return null;
        if (DateTime.UtcNow - _generatedAt > Expiry)
        {
            _currentCode = null;
            return null;
        }
        return _currentCode;
    }

    public static bool VerifyCode(string code)
    {
        var stored = GetCurrentCode();
        if (stored == null || code != stored) return false;
        Clear();
        return true;
    }

    public static void Clear()
    {
        _currentCode = null;
        _generatedAt = DateTime.MinValue;
    }
}
