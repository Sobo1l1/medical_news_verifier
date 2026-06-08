namespace MedicalNewsVerifier.Web;

public static class AnalysisRunSettingsValidator
{
    public static List<string> Validate(AnalysisRunSettings? settings)
    {
        if (settings is null)
        {
            return [];
        }

        var errors = new List<string>();

        if (settings.MaxCorpusSnippets is < 1 or > 8)
        {
            errors.Add("Фрагментов корпуса для LLM: от 1 до 8.");
        }

        if (settings.MaxCorpusCharsPerSnippet is < 500 or > 4000)
        {
            errors.Add("Символов на фрагмент: от 500 до 4000.");
        }

        if (settings.MaxResponseTokens is < 500 or > 8000)
        {
            errors.Add("Токенов ответа Ollama: от 500 до 8000.");
        }

        if (settings.Temperature is < 0 or > 1.5)
        {
            errors.Add("Temperature: от 0 до 1.5.");
        }

        if (settings.TopP is < 0.1 or > 1.0)
        {
            errors.Add("TopP: от 0.1 до 1.0.");
        }

        if (settings.MaxArticlesPerAnalysis is < 1 or > 10)
        {
            errors.Add("Статей из источников: от 1 до 10.");
        }

        if (settings.MinRelevanceScore is < 5 or > 50)
        {
            errors.Add("Порог релевантности: от 5 до 50.");
        }

        if (settings.MinzdravMaxFeedScan is < 50 or > 400)
        {
            errors.Add("Скан ленты Минздрава: от 50 до 400.");
        }

        if (settings.HeuristicBlendWeight is < 0.1 or > 0.9)
        {
            errors.Add("Доля признакового анализа: от 0.1 до 0.9.");
        }

        if (settings.LlmBlendWeight is < 0.1 or > 0.9)
        {
            errors.Add("Доля нейросети: от 0.1 до 0.9.");
        }

        if (settings.PythonTimeoutSeconds is < 5 or > 120)
        {
            errors.Add("Таймаут Python: от 5 до 120 сек.");
        }

        return errors;
    }

    public static AnalysisRunSettings Clamp(AnalysisRunSettings? settings)
    {
        if (settings is null)
        {
            return new AnalysisRunSettings();
        }

        return new AnalysisRunSettings
        {
            OllamaEnabled = settings.OllamaEnabled,
            MaxCorpusSnippets = ClampInt(settings.MaxCorpusSnippets, 1, 8),
            MaxCorpusCharsPerSnippet = ClampInt(settings.MaxCorpusCharsPerSnippet, 500, 4000),
            MaxResponseTokens = ClampInt(settings.MaxResponseTokens, 500, 8000),
            Temperature = ClampDouble(settings.Temperature, 0, 1.5),
            TopP = ClampDouble(settings.TopP, 0.1, 1.0),
            EnableThinking = settings.EnableThinking,
            MaxArticlesPerAnalysis = ClampInt(settings.MaxArticlesPerAnalysis, 1, 10),
            MinRelevanceScore = ClampInt(settings.MinRelevanceScore, 5, 50),
            MinzdravMaxFeedScan = ClampInt(settings.MinzdravMaxFeedScan, 50, 400),
            HeuristicBlendWeight = ClampDouble(settings.HeuristicBlendWeight, 0.1, 0.9),
            LlmBlendWeight = ClampDouble(settings.LlmBlendWeight, 0.1, 0.9),
            PythonTimeoutSeconds = ClampInt(settings.PythonTimeoutSeconds, 5, 120),
            PythonEnableNatasha = settings.PythonEnableNatasha,
            PythonEnableStanza = settings.PythonEnableStanza
        };
    }

    private static int? ClampInt(int? value, int min, int max) =>
        value.HasValue ? Math.Clamp(value.Value, min, max) : null;

    private static double? ClampDouble(double? value, double min, double max) =>
        value.HasValue ? Math.Clamp(value.Value, min, max) : null;
}
