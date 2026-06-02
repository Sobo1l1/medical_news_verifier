using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MedicalNewsVerifier.Web.Data;

public static partial class NewsContentFingerprint
{
    public static string Compute(string headline, string newsText)
    {
        var normalized = $"{Normalize(headline)}\n{Normalize(newsText)}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Normalize(string text) =>
        MultiSpaceRegex().Replace(text.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
