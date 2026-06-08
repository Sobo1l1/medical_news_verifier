namespace MedicalNewsVerifier.Web;

/// <summary>Переопределения на одну проверку; null = значение из appsettings.</summary>
public sealed class AnalysisRunSettings
{
    public bool? OllamaEnabled { get; set; }
    public int? MaxCorpusSnippets { get; set; }
    public int? MaxCorpusCharsPerSnippet { get; set; }
    public int? MaxResponseTokens { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public bool? EnableThinking { get; set; }
    public int? MaxArticlesPerAnalysis { get; set; }
    public int? MinRelevanceScore { get; set; }
    public int? MinzdravMaxFeedScan { get; set; }
    public double? HeuristicBlendWeight { get; set; }
    public double? LlmBlendWeight { get; set; }
    public int? PythonTimeoutSeconds { get; set; }
    public bool? PythonEnableNatasha { get; set; }
    public bool? PythonEnableStanza { get; set; }
}

/// <summary>Итоговые значения после слияния с appsettings и clamp.</summary>
public sealed class EffectiveAnalysisRunSettings
{
    public bool OllamaEnabled { get; init; }
    public bool OllamaGloballyEnabled { get; init; }
    public int MaxCorpusSnippets { get; init; }
    public int MaxCorpusCharsPerSnippet { get; init; }
    public int MaxResponseTokens { get; init; }
    public double Temperature { get; init; }
    public double TopP { get; init; }
    public bool EnableThinking { get; init; }
    public int MaxArticlesPerAnalysis { get; init; }
    public int MinRelevanceScore { get; init; }
    public int MinzdravMaxFeedScan { get; init; }
    public double HeuristicBlendWeight { get; init; }
    public double LlmBlendWeight { get; init; }
    public int PythonTimeoutSeconds { get; init; }
    public bool PythonEnableNatasha { get; init; }
    public bool PythonEnableStanza { get; init; }

    public bool HasCustomOverrides { get; init; }

    public string ToSummaryLine()
    {
        if (!HasCustomOverrides)
        {
            return "Параметры проверки: стандартные (appsettings).";
        }

        return $"Параметры проверки (override): Ollama={(OllamaEnabled ? "вкл" : "выкл")}, " +
               $"статей={MaxArticlesPerAnalysis}, порог={MinRelevanceScore}, " +
               $"фрагм.LLM={MaxCorpusSnippets}, веса={HeuristicBlendWeight:F2}/{LlmBlendWeight:F2}.";
    }
}
