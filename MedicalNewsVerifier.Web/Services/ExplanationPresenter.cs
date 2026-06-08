using System.Text.RegularExpressions;

namespace MedicalNewsVerifier.Web.Services;

public sealed class ExplanationDisplayLine
{
    public required string Text { get; init; }
    public string IconClass { get; init; } = "bi-info-circle text-primary";
}

public sealed class ExplanationDisplayModel
{
    public IReadOnlyList<ExplanationDisplayLine> SummaryLines { get; init; } = [];
    public IReadOnlyList<string> TechnicalLines { get; init; } = [];
}

public static partial class ExplanationPresenter
{
    public static ExplanationDisplayModel Parse(string? explanation, string? llmSummary = null)
    {
        if (string.IsNullOrWhiteSpace(explanation))
        {
            return new ExplanationDisplayModel();
        }

        var hasLlmSummary = !string.IsNullOrWhiteSpace(llmSummary);
        var summary = new List<ExplanationDisplayLine>();
        var technical = new List<string>();

        foreach (var raw in explanation.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (ShouldSkip(raw, hasLlmSummary))
            {
                continue;
            }

            if (IsTechnical(raw))
            {
                technical.Add(raw);
            }
            else
            {
                summary.Add(new ExplanationDisplayLine
                {
                    Text = Humanize(raw, hasLlmSummary),
                    IconClass = PickIcon(raw)
                });
            }
        }

        return new ExplanationDisplayModel
        {
            SummaryLines = summary,
            TechnicalLines = technical
        };
    }

    private static bool ShouldSkip(string line, bool hasLlmSummary) =>
        hasLlmSummary && line.Contains("Кратко:", StringComparison.Ordinal);

    private static bool IsTechnical(string line)
    {
        if (line.StartsWith("Параметры проверки", StringComparison.Ordinal))
        {
            return true;
        }

        if (line.Contains("(C#)", StringComparison.Ordinal)
            || line.Contains("приложения (C#", StringComparison.Ordinal)
            || line.Contains("словарь, C#", StringComparison.Ordinal))
        {
            return true;
        }

        if (line.StartsWith("Дополнительный модуль (Python)", StringComparison.Ordinal)
            || line.Contains("Python:ScriptPath", StringComparison.Ordinal)
            || line.Contains("Python:ExecutablePath", StringComparison.Ordinal)
            || line.Contains("Python:TimeoutSeconds", StringComparison.Ordinal))
        {
            return true;
        }

        if (line.Contains("Ollama:Enabled", StringComparison.Ordinal))
        {
            return true;
        }

        if (line.StartsWith("Счётчики эмоциональной", StringComparison.Ordinal))
        {
            return true;
        }

        if (line.StartsWith("Эмоциональная лексика", StringComparison.Ordinal)
            || line.StartsWith("Верхний регистр", StringComparison.Ordinal)
            || line.StartsWith("Даты (", StringComparison.Ordinal)
            || line.StartsWith("Ссылки в тексте", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string Humanize(string line, bool hasLlmSummary)
    {
        var combined = CombinedScoreRegex().Replace(line, "Итоговая оценка: $1 из 100.");
        var heuristic = HeuristicScoreRegex().Replace(combined, "Признаковый анализ: $1 из 100.");

        if (hasLlmSummary && heuristic.StartsWith("Нейросеть (Ollama):", StringComparison.Ordinal))
        {
            var scoreMatch = LlmScoreRegex().Match(heuristic);
            if (scoreMatch.Success)
            {
                return $"Нейросеть Ollama: согласованность с корпусом — {scoreMatch.Groups[1].Value} из 100.";
            }

            if (heuristic.Contains("отключена", StringComparison.OrdinalIgnoreCase))
            {
                return "Нейросеть Ollama не использовалась — итог рассчитан по признакам.";
            }

            if (heuristic.Contains("не удалось", StringComparison.OrdinalIgnoreCase))
            {
                return "Нейросеть Ollama: оценка недоступна, итог рассчитан по признакам.";
            }
        }

        if (heuristic.StartsWith("Выделено фрагментов", StringComparison.Ordinal))
        {
            var m = FragmentCountRegex().Match(heuristic);
            return m.Success
                ? $"В тексте отмечено {m.Groups[1].Value} фрагмент(ов), требующих внимания."
                : heuristic;
        }

        if (heuristic.StartsWith("Релевантных официальных", StringComparison.Ordinal))
        {
            var m = MatchCountRegex().Match(heuristic);
            return m.Success
                ? $"Найдено релевантных официальных публикаций: {m.Groups[1].Value}."
                : heuristic;
        }

        return heuristic;
    }

    private static string PickIcon(string line)
    {
        if (line.StartsWith("Итоговая оценка", StringComparison.Ordinal)
            || line.Contains("Итоговая оценка:", StringComparison.Ordinal))
        {
            return "bi-award text-primary";
        }

        if (line.StartsWith("Оценка по признакам", StringComparison.Ordinal)
            || line.Contains("Признаковый анализ:", StringComparison.Ordinal))
        {
            return "bi-list-check text-primary";
        }

        if (line.StartsWith("Нейросеть", StringComparison.Ordinal))
        {
            return "bi-cpu text-primary";
        }

        if (line.StartsWith("Выделено фрагментов", StringComparison.Ordinal)
            || line.Contains("отмечено", StringComparison.Ordinal))
        {
            return "bi-highlighter text-warning";
        }

        if (line.StartsWith("Сопоставление", StringComparison.Ordinal))
        {
            return line.Contains("не найдено", StringComparison.Ordinal)
                ? "bi-exclamation-triangle text-warning"
                : "bi-check-circle text-success";
        }

        if (line.StartsWith("Оценка нейросети", StringComparison.Ordinal))
        {
            return "bi-shield-exclamation text-muted";
        }

        if (line.StartsWith("Релевантных", StringComparison.Ordinal)
            || line.Contains("Найдено релевантных", StringComparison.Ordinal))
        {
            return "bi-journal-text text-primary";
        }

        return "bi-info-circle text-secondary";
    }

    [GeneratedRegex(@"Итоговая оценка достоверности \(комбинированная\):\s*(\d+)\s*из\s*100", RegexOptions.IgnoreCase)]
    private static partial Regex CombinedScoreRegex();

    [GeneratedRegex(@"Оценка по признакам[^:]*:\s*(\d+)\s*из\s*100", RegexOptions.IgnoreCase)]
    private static partial Regex HeuristicScoreRegex();

    [GeneratedRegex(@"согласованность с выдержками корпуса\s*—\s*(\d+)\s*из\s*100", RegexOptions.IgnoreCase)]
    private static partial Regex LlmScoreRegex();

    [GeneratedRegex(@"Выделено фрагментов[^:]*:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex FragmentCountRegex();

    [GeneratedRegex(@"Релевантных официальных публикаций[^:]*:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MatchCountRegex();
}
