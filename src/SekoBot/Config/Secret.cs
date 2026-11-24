using System.Security.Cryptography;
using System.Text;

namespace SekoBot.Config;

/// <summary>
/// Secrets (the Telegram bot token) kept in the settings files encrypted with Windows' own protection (DPAPI):
/// only this Windows user on this PC can read them back; a copied file holds nothing usable.
/// </summary>
internal static class Secret
{
    private const string Mark = "dpapi:";
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("SekoBot.Telegram");

    public static bool IsProtected(string? stored) => stored?.StartsWith(Mark, StringComparison.Ordinal) == true;

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain))
            return "";
        try
        {
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Salt, DataProtectionScope.CurrentUser);
            return Mark + Convert.ToBase64String(data);
        }
        catch (CryptographicException) { return ""; }
    }

    /// <summary>The secret back; a value from an older version (not yet encrypted) as it is; "" when it can't be read
    /// (another PC or Windows user).</summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return "";
        if (!IsProtected(stored))
            return stored;
        try
        {
            byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(stored[Mark.Length..]), Salt, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return ""; }
    }
}
