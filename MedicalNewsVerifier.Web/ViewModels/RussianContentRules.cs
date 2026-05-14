namespace MedicalNewsVerifier.Web.ViewModels;

/// <summary>Проверка, что буквенный состав текста преимущественно кириллица (русский язык).</summary>
public static class RussianContentRules
{
    /// <summary>Минимальная доля кириллических букв среди всех букв (латиница+кириллица). Короткие строки не проверяются.</summary>
    public const double MinCyrillicLetterRatio = 0.52;

    /// <summary>Минимум букв для применения доли (иначе пропускаем — короткие заголовки).</summary>
    public const int MinLettersForRatioCheck = 8;

    public static bool IsPredominantlyRussian(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var cyrillic = 0;
        var latin = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var v = rune.Value;
            if (IsCyrillicLetter(v))
            {
                cyrillic++;
            }
            else if (IsBasicLatinLetter(v))
            {
                latin++;
            }
        }

        var letters = cyrillic + latin;
        if (letters < MinLettersForRatioCheck)
        {
            return cyrillic >= 1;
        }

        return (double)cyrillic / letters >= MinCyrillicLetterRatio;
    }

    private static bool IsCyrillicLetter(int codePoint) =>
        (codePoint is >= 0x0400 and <= 0x04FF) ||
        (codePoint is >= 0x0500 and <= 0x052F);

    private static bool IsBasicLatinLetter(int codePoint) =>
        (codePoint is >= 'A' and <= 'Z') || (codePoint is >= 'a' and <= 'z');
}
