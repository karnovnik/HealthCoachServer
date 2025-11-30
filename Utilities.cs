using System.Text.Json;
using System.Security.Cryptography;
using System.Text;


namespace HealthCoachServer;
public static class Utilities
{
    public static string HashIfNeeded(string input)
    {
        if (string.IsNullOrEmpty(input)) return "anon";
        using var sha = SHA256.Create();
        var h = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(h).ToLowerInvariant();
    }


    public static string[] ParseExtraParams(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        raw = raw.Trim();
        try { if (raw.StartsWith("[") && raw.EndsWith("]")) return JsonSerializer.Deserialize<string[]>(raw) ?? Array.Empty<string>(); }
        catch { }
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
    }


    public static string EscapeForPrompt(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace('\r', ' ').Replace('\n', ' ').Replace('"', '\'');


    public static object TryParseJsonOrString(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try { return JsonSerializer.Deserialize<object>(text); } catch { return text; }
    }
}