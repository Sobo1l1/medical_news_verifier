namespace MedicalNewsVerifier.Web.Services;

/// <summary>Сопоставление токена со словарём с учётом словоформ (корень 4 символа).</summary>
public static class LexiconMatcher
{
    public static HashSet<string> BuildRoot4Index(IEnumerable<string> words) =>
        words.Where(w => w.Length >= 4).Select(w => w[..4]).ToHashSet(StringComparer.Ordinal);

    public static bool Matches(HashSet<string> lexicon, HashSet<string> root4Index, string cleaned, string lemma)
    {
        if (string.IsNullOrEmpty(cleaned))
        {
            return false;
        }

        if (lexicon.Contains(cleaned) || lexicon.Contains(lemma))
        {
            return true;
        }

        if (cleaned.Length >= 4 && root4Index.Contains(cleaned[..4]))
        {
            return true;
        }

        return lemma.Length >= 4 && root4Index.Contains(lemma[..4]);
    }
}
