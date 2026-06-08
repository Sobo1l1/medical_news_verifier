namespace MedicalNewsVerifier.Web.Services;

public interface IAnalysisDefaultsService
{
    EffectiveAnalysisRunSettings GetDefaults();
    EffectiveAnalysisRunSettings Resolve(AnalysisRunSettings? overrides);
    bool IsDifferentFromDefaults(AnalysisRunSettings? overrides);
}

public sealed class AnalysisDefaultsService(IConfiguration configuration) : IAnalysisDefaultsService
{
    public EffectiveAnalysisRunSettings GetDefaults() => Resolve(null);

    public EffectiveAnalysisRunSettings Resolve(AnalysisRunSettings? overrides)
    {
        var clamped = AnalysisRunSettingsValidator.Clamp(overrides);
        var globalOllama = configuration.GetValue("Ollama:Enabled", false);

        var wh = clamped.HeuristicBlendWeight
            ?? configuration.GetValue("AnalysisScoring:HeuristicBlendWeight", 0.65);
        var wl = clamped.LlmBlendWeight
            ?? configuration.GetValue("AnalysisScoring:LlmBlendWeight", 0.35);
        NormalizeWeights(ref wh, ref wl);

        var requestedOllama = clamped.OllamaEnabled ?? globalOllama;
        var effectiveOllama = globalOllama && requestedOllama;

        var hasOverrides = overrides is not null && HasAnyOverride(clamped);

        return new EffectiveAnalysisRunSettings
        {
            OllamaGloballyEnabled = globalOllama,
            OllamaEnabled = effectiveOllama,
            MaxCorpusSnippets = clamped.MaxCorpusSnippets
                ?? configuration.GetValue("Ollama:MaxCorpusSnippets", 4),
            MaxCorpusCharsPerSnippet = clamped.MaxCorpusCharsPerSnippet
                ?? configuration.GetValue("Ollama:MaxCorpusCharsPerSnippet", 1200),
            MaxResponseTokens = clamped.MaxResponseTokens
                ?? configuration.GetValue("Ollama:MaxResponseTokens", 3000),
            Temperature = clamped.Temperature
                ?? configuration.GetValue("Ollama:Temperature", 0.2),
            TopP = clamped.TopP
                ?? configuration.GetValue("Ollama:TopP", 1.0),
            EnableThinking = clamped.EnableThinking
                ?? configuration.GetValue("Ollama:EnableThinking", false),
            MaxArticlesPerAnalysis = clamped.MaxArticlesPerAnalysis
                ?? configuration.GetValue("SourceParsers:MaxArticlesPerAnalysis", 5),
            MinRelevanceScore = clamped.MinRelevanceScore
                ?? configuration.GetValue("SourceParsers:MinRelevanceScore", 15),
            MinzdravMaxFeedScan = clamped.MinzdravMaxFeedScan
                ?? configuration.GetValue("SourceParsers:Minzdrav:MaxFeedScan", 400),
            HeuristicBlendWeight = wh,
            LlmBlendWeight = wl,
            PythonTimeoutSeconds = clamped.PythonTimeoutSeconds
                ?? configuration.GetValue("Python:TimeoutSeconds", 30),
            PythonEnableNatasha = clamped.PythonEnableNatasha
                ?? configuration.GetValue("Python:EnableNatasha", false),
            PythonEnableStanza = clamped.PythonEnableStanza
                ?? configuration.GetValue("Python:EnableStanza", false),
            HasCustomOverrides = hasOverrides
        };
    }

    public bool IsDifferentFromDefaults(AnalysisRunSettings? overrides)
    {
        if (overrides is null)
        {
            return false;
        }

        var defaults = GetDefaults();
        var resolved = Resolve(overrides);

        return resolved.OllamaEnabled != defaults.OllamaEnabled
            || resolved.MaxCorpusSnippets != defaults.MaxCorpusSnippets
            || resolved.MaxCorpusCharsPerSnippet != defaults.MaxCorpusCharsPerSnippet
            || resolved.MaxResponseTokens != defaults.MaxResponseTokens
            || Math.Abs(resolved.Temperature - defaults.Temperature) > 0.001
            || Math.Abs(resolved.TopP - defaults.TopP) > 0.001
            || resolved.EnableThinking != defaults.EnableThinking
            || resolved.MaxArticlesPerAnalysis != defaults.MaxArticlesPerAnalysis
            || resolved.MinRelevanceScore != defaults.MinRelevanceScore
            || resolved.MinzdravMaxFeedScan != defaults.MinzdravMaxFeedScan
            || Math.Abs(resolved.HeuristicBlendWeight - defaults.HeuristicBlendWeight) > 0.001
            || Math.Abs(resolved.LlmBlendWeight - defaults.LlmBlendWeight) > 0.001
            || resolved.PythonTimeoutSeconds != defaults.PythonTimeoutSeconds
            || resolved.PythonEnableNatasha != defaults.PythonEnableNatasha
            || resolved.PythonEnableStanza != defaults.PythonEnableStanza;
    }

    private static void NormalizeWeights(ref double wh, ref double wl)
    {
        wh = Math.Clamp(wh, 0.1, 0.9);
        wl = Math.Clamp(wl, 0.1, 0.9);
        var sum = wh + wl;
        if (sum <= 0)
        {
            wh = 0.65;
            wl = 0.35;
            return;
        }

        wh /= sum;
        wl /= sum;
    }

    private static bool HasAnyOverride(AnalysisRunSettings s) =>
        s.OllamaEnabled.HasValue
        || s.MaxCorpusSnippets.HasValue
        || s.MaxCorpusCharsPerSnippet.HasValue
        || s.MaxResponseTokens.HasValue
        || s.Temperature.HasValue
        || s.TopP.HasValue
        || s.EnableThinking.HasValue
        || s.MaxArticlesPerAnalysis.HasValue
        || s.MinRelevanceScore.HasValue
        || s.MinzdravMaxFeedScan.HasValue
        || s.HeuristicBlendWeight.HasValue
        || s.LlmBlendWeight.HasValue
        || s.PythonTimeoutSeconds.HasValue
        || s.PythonEnableNatasha.HasValue
        || s.PythonEnableStanza.HasValue;
}
