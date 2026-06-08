using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MedicalNewsVerifier.Web.Models;
using Microsoft.Extensions.Options;

namespace MedicalNewsVerifier.Web.Services;

public sealed class OllamaOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    public string Model { get; set; } = "qwen3.5:9b";
    public int TimeoutSeconds { get; set; } = 300;
    public int MaxCorpusCharsPerSnippet { get; set; } = 2200;
    public int MaxCorpusSnippets { get; set; } = 4;
    /// <summary>Максимальное число токенов в ответе от модели. По умолчанию ~2000 токенов ≈ 5-6 КБ текста.</summary>
    public int MaxResponseTokens { get; set; } = 2000;
    /// <summary>
    /// Пытаемся включить принудительный JSON-режим в OpenAI-совместимом API (response_format: json_object).
    /// Важно: поддерживается не всеми моделями/версиями Ollama.
    /// </summary>
    public bool ForceJsonResponseFormat { get; set; } = true;

    /// <summary>
    /// Предпочитать нативный Ollama API (/api/chat) с format="json" (самый надёжный способ получить JSON без thinking).
    /// </summary>
    public bool PreferNativeApi { get; set; } = true;

    /// <summary>
    /// Включить режим «thinking» у моделей вроде Qwen3. Для JSON-ответов рекомендуется false.
    /// </summary>
    public bool EnableThinking { get; set; }

    /// <summary>Температура генерации (0 — детерминированнее, выше — разнообразнее). Диапазон 0–2.</summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>Nucleus sampling (top_p). Обычно 0.9–1.0 для фактчекинга.</summary>
    public double TopP { get; set; } = 1.0;
}

public sealed partial class OllamaComparisonClient(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaComparisonClient> logger) : IOllamaComparisonClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly JsonDocumentOptions RelaxedJsonDocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public async Task<OllamaComparisonOutcome> CompareNewsToCorpusAsync(
        string headline,
        string newsBody,
        IReadOnlyList<OfficialPublication> corpusExcerpts,
        CancellationToken cancellationToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            return new OllamaComparisonOutcome
            {
                WasAttempted = false,
                Succeeded = false,
                ErrorMessage = "Сравнение через Ollama отключено в конфигурации (Ollama:Enabled)."
            };
        }

        if (string.IsNullOrWhiteSpace(opt.Model))
        {
            return new OllamaComparisonOutcome
            {
                WasAttempted = false,
                Succeeded = false,
                ErrorMessage = "Не задана модель Ollama (Ollama:Model)."
            };
        }

        var userNews = $"{headline.Trim()}\n\n{newsBody.Trim()}".Trim();
        if (userNews.Length == 0)
        {
            return new OllamaComparisonOutcome
            {
                WasAttempted = false,
                Succeeded = false,
                ErrorMessage = "Пустой текст новости."
            };
        }

        var corpusIsWeak = IsCorpusWeakForFactCheck(corpusExcerpts);
        var corpusBlock = BuildCorpusBlock(corpusExcerpts, opt.MaxCorpusSnippets, opt.MaxCorpusCharsPerSnippet);
        var systemPrompt =
            """
            Ты — медицинский фактчекер. Сравни новость с выдержками корпуса.

            Поле summary обязательно и всегда заполнено (2–4 предложения на русском). Запрещено отвечать одной фразой вроде «информации нет» или «проверить невозможно» без продолжения.

            Структура summary:
            1) Что показал корпус (совпадения, пробелы, нерелевантность выдержек).
            2) Предварительная оценка новости по общепринятым медицинским знаниям — даже если корпус пуст или не по теме. Явно пометь, что это экспертная оценка модели, а не цитата из корпуса.
            Итог: пользователь всегда получает и вывод по корпусу, и осторожный медицинский комментарий.

            alignmentScore (0–100) — оценка ДОСТОВЕРНОСТИ и согласованности новости с корпусом и медицинским консенсусом:
            - 75–100: тезисы новости подтверждаются корпусом или не противоречат ему и общепринятой медицине.
            - 45–74: частичное совпадение или неоднозначность; нужна дополнительная проверка фактов.
            - 20–44: существенные пробелы, преувеличения или слабая опора на источники.
            - 0–19: явные противоречия корпусу/медицинским знаниям, мифы, конспирология, дезинформация.

            Важно: если корпус не обсуждает тему новости, но по общим медицинским знаниям утверждения новости ложны или вредны — ставь 0–25, а не средние 35–65. Средние баллы (35–65) только когда новость в целом правдоподобна, но корпус её не покрывает.

            Верни строго один JSON-объект (без markdown):
            {"alignmentScore":0-100,"summary":"...","flags":["snake_case"]}

            Пример: корпус не по теме, но новость правдоподобна:
            {"alignmentScore":52,"summary":"В выдержках корпуса нет статистики по теме новости. С учётом общих медицинских знаний рекомендации звучат правдоподобно, но цифры нужно сверить с официальной отчётностью.","flags":["insufficient_corpus","requires_verification"]}

            Пример: корпус подтверждает новость:
            {"alignmentScore":82,"summary":"Корпус подтверждает ключевые тезисы о профилактике. Формулировки согласуются с официальными рекомендациями.","flags":["accurate"]}

            Пример: новость противоречит корпусу и медицинским знаниям:
            {"alignmentScore":12,"summary":"Корпус содержит официальные рекомендации по профилактике ОРВИ; утверждения новости о вреде вакцин и конспирологические тезисы им не соответствуют и противоречат медицинскому консенсусу.","flags":["unsupported_claims","possible_contradiction"]}

            flags: insufficient_corpus, requires_verification, possible_contradiction, unsupported_claims, accurate.
            """;

        var userPrompt =
            $"""
            НОВОСТЬ ПОЛЬЗОВАТЕЛЯ:
            {userNews}

            ВЫДЕРЖКИ ИЗ ДОВЕРЕННОГО КОРПУСА:
         
            {corpusBlock}
            """;

        try
        {
            var parsed = await TryGetParsedModelJsonAsync(
                opt,
                systemPrompt,
                userPrompt,
                cancellationToken);

            if (parsed is null)
            {
                return new OllamaComparisonOutcome
                {
                    WasAttempted = true,
                    Succeeded = false,
                    ErrorMessage = "Модель вернула ответ, который не удалось разобрать как JSON. Проверьте логи приложения."
                };
            }

            var normalizedSummary = NormalizeSummary(parsed.Summary);
            int? score = parsed.AlignmentScore;
            if (score.HasValue)
            {
                var raw = Math.Clamp(score.Value, 0, 100);
                score = CalibrateAlignmentScore(raw, parsed.Flags, normalizedSummary);
                if (score != raw)
                {
                    logger.LogInformation(
                        "Ollama: alignmentScore скорректирован {Raw} → {Adjusted} (flags/summary)",
                        raw,
                        score);
                }
            }

            var summary = EnsureSubstantiveSummary(
                normalizedSummary,
                parsed.Flags,
                score,
                corpusIsWeak);

            if (summary?.Length > 8000)
            {
                summary = summary[..7997] + "…";
            }

            if (string.IsNullOrWhiteSpace(summary) && score.HasValue)
            {
                logger.LogWarning(
                    "Ollama: JSON распознан (score={Score}), summary восстановлен из запасного шаблона",
                    score);
                summary = BuildFallbackSummary(score.Value, corpusIsWeak);
            }

            return new OllamaComparisonOutcome
            {
                WasAttempted = true,
                Succeeded = true,
                AlignmentScore = score,
                Summary = summary
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ollama request failed");
            return new OllamaComparisonOutcome
            {
                WasAttempted = true,
                Succeeded = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<LlmJsonPayload?> TryGetParsedModelJsonAsync(
        OllamaOptions opt,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        if (opt.PreferNativeApi)
        {
            var fromNative = await TryParseNativeAsync(opt, systemPrompt, userPrompt, cancellationToken);
            if (fromNative is not null)
            {
                return fromNative;
            }
        }

        // OpenAI-совместимый endpoint (/v1/chat/completions), если BaseUrl на него указывает.
        if (opt.BaseUrl.Contains("/v1", StringComparison.OrdinalIgnoreCase))
        {
            var (openAiText, openAiTextLen) = await TryCallOpenAiChatCompletionsAsync(opt, systemPrompt, userPrompt, cancellationToken);
            if (!string.IsNullOrWhiteSpace(openAiText))
            {
                var parsed = ParseModelJson(openAiText);
                if (parsed is not null)
                {
                    return parsed;
                }

                logger.LogWarning("Ollama: не удалось разобрать JSON (OpenAI /v1). Длина: {Length} chars. Первые 500 символов: {Preview}",
                    openAiTextLen,
                    Truncate(openAiText, 500));
            }
        }

        if (!opt.PreferNativeApi)
        {
            var fromNative = await TryParseNativeAsync(opt, systemPrompt, userPrompt, cancellationToken);
            if (fromNative is not null)
            {
                return fromNative;
            }
        }

        return null;
    }

    private async Task<LlmJsonPayload?> TryParseNativeAsync(
        OllamaOptions opt,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Ollama: trying native /api/chat format=json");

        var nativeText = await TryCallNativeOllamaChatAsync(opt, systemPrompt, userPrompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(nativeText))
        {
            logger.LogWarning("Ollama: native /api/chat returned empty content");
            return null;
        }

        var parsed = ParseModelJson(nativeText);
        if (parsed is not null)
        {
            return parsed;
        }

        logger.LogWarning("Ollama: не удалось разобрать JSON (native /api/chat). Длина: {Length} chars. Первые 500 символов: {Preview}",
            nativeText.Length,
            Truncate(nativeText, 500));
        return null;
    }

    private async Task<(string Text, int Length)> TryCallOpenAiChatCompletionsAsync(
        OllamaOptions opt,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var payload = new ChatCompletionRequest
        {
            Model = opt.Model.Trim(),
            Stream = false,
            Temperature = ClampSampling(opt.Temperature),
            TopP = ClampTopP(opt.TopP),
            MaxTokens = opt.MaxResponseTokens,
            Think = opt.EnableThinking ? null : false,
            ResponseFormat = opt.ForceJsonResponseFormat ? new ResponseFormat { Type = "json_object" } : null,
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userPrompt }
            ]
        };

        var requestBody = JsonSerializer.Serialize(payload, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Ollama HTTP {Status}: {Body}", (int)response.StatusCode, Truncate(body, 500));
            return (string.Empty, 0);
        }

        logger.LogDebug("Ollama response body length={Length}", body.Length);
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var messageObj))
        {
            logger.LogWarning("Ollama: некорректная структура ответа (/v1)");
            return (string.Empty, 0);
        }

        var content = messageObj.TryGetProperty("content", out var contentProp)
            ? contentProp.GetString() ?? string.Empty
            : string.Empty;

        var reasoning = messageObj.TryGetProperty("reasoning", out var reasoningProp)
            ? (reasoningProp.GetString() ?? string.Empty)
            : string.Empty;

        if (string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(reasoning))
        {
            logger.LogWarning("Ollama: content пуст, пробуем извлечь JSON из reasoning (длина {Length})", reasoning.Length);
        }

        var parseInput = SelectParseInput(content, reasoning);

        return (parseInput, parseInput.Length);
    }

    private async Task<string> TryCallNativeOllamaChatAsync(
        OllamaOptions opt,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        // Преобразуем BaseUrl "http://host:11434/v1" -> "http://host:11434"
        var baseUrl = opt.BaseUrl.Trim().TrimEnd('/');
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^3];
        }

        var url = $"{baseUrl}/api/chat";
        var payload = new NativeChatRequest
        {
            Model = opt.Model.Trim(),
            Stream = false,
            Format = "json",
            Think = opt.EnableThinking,
            Messages =
            [
                new NativeChatMessage { Role = "system", Content = systemPrompt },
                new NativeChatMessage { Role = "user", Content = userPrompt }
            ],
            Options = new NativeChatOptions
            {
                Temperature = ClampSampling(opt.Temperature),
                TopP = ClampTopP(opt.TopP),
                NumPredict = opt.MaxResponseTokens
            }
        };

        var requestBody = JsonSerializer.Serialize(payload, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Ollama native HTTP {Status}: {Body}", (int)response.StatusCode, Truncate(body, 500));
            return string.Empty;
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("message", out var msg))
        {
            logger.LogWarning("Ollama: некорректная структура ответа (native /api/chat)");
            return string.Empty;
        }

        var content = msg.TryGetProperty("content", out var contentProp)
            ? contentProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(content) && msg.TryGetProperty("thinking", out var thinkingProp))
        {
            var thinking = thinkingProp.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(thinking))
            {
                logger.LogWarning("Ollama native: content пуст, пробуем JSON из thinking (длина {Length})", thinking.Length);
                content = thinking;
            }
        }

        return content;
    }

    private static string BuildCorpusBlock(IReadOnlyList<OfficialPublication> corpus, int maxSnippets, int maxChars)
    {
        if (corpus.Count == 0)
        {
            return """
                (Релевантных выдержек в корпусе нет. В summary обязательно укажи это и добавь предварительную оценку новости по общим медицинским знаниям — не ограничивайся фразой «проверить невозможно». В flags добавь insufficient_corpus.)
                """;
        }

        var sb = new StringBuilder();
        var n = Math.Min(maxSnippets, corpus.Count);
        for (var i = 0; i < n; i++)
        {
            var p = corpus[i];
            var excerpt = p.Content;
            if (excerpt.Length > maxChars)
            {
                excerpt = excerpt[..maxChars] + "…";
            }

            sb.AppendLine($"--- Источник {i + 1}: {p.SourceName} ---");
            sb.AppendLine($"URL: {p.Url}");
            sb.AppendLine($"Заголовок: {p.Title}");
            sb.AppendLine(excerpt);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static LlmJsonPayload? ParseModelJson(string content)
    {
        var trimmed = content.Trim();

        // Агрессивно удаляем все thinking блоки (разные форматы)
        trimmed = Regex.Replace(trimmed, @"<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"<thinking>.*?</thinking>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"```think.*?```", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"\*\*Think:\*\*.*?(?=\{)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        // Удаляем Qwen-стиль thinking (начинается с "Thinking Process:" и идёт до первого {)
        trimmed = Regex.Replace(trimmed, @"^.*?Thinking\s+Process:.*?(?=\{)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        // Удаляем весь текст типа "1. **Analyze the Request:**" и подобные структурированные рассуждения до первого {
        trimmed = Regex.Replace(trimmed, @"^\s*\d+\s*\.\s*\*\*.*?(?=\{)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Удаляем markdown ограждения ```json / ```
        trimmed = MarkdownFenceRegex().Replace(trimmed, "").Trim();

        // Пробуем распарсить любой валидный JSON-объект из ответа (с разрешением trailing commas).
        LlmJsonPayload? firstParsed = null;
        LlmJsonPayload? bestParsed = null;
        foreach (var json in ExtractJsonObjects(trimmed))
        {
            var parsed = TryDeserializePayload(json);
            if (parsed is null)
            {
                continue;
            }

            parsed.Summary = NormalizeSummary(parsed.Summary);
            firstParsed ??= parsed;

            if (parsed.AlignmentScore is not null && !string.IsNullOrWhiteSpace(parsed.Summary))
            {
                return parsed;
            }

            if (parsed.AlignmentScore is not null)
            {
                bestParsed = parsed;
            }
        }

        if (bestParsed is not null)
        {
            return bestParsed;
        }

        // Если JSON валиден, но alignmentScore не нашли — всё равно вернём первый распарсенный объект.
        if (firstParsed is not null)
        {
            return firstParsed;
        }

        // Последняя попытка: вытащить хотя бы alignmentScore/summary из JSON-подобного текста.
        return TrySalvageFromText(trimmed);
    }

    private static LlmJsonPayload? TryDeserializePayload(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, RelaxedJsonDocOptions);
            var normalized = doc.RootElement.GetRawText();
            return JsonSerializer.Deserialize<LlmJsonPayload>(normalized, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> ExtractJsonObjects(string s)
    {
        var start = -1;
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];

            if (inString)
            {
                switch (ch)
                {
                    case '_' when escape:
                        escape = false;
                        break;
                    case '\\':
                        escape = true;
                        break;
                    case '"':
                        inString = false;
                        break;
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    if (depth == 0)
                    {
                        start = i;
                    }
                    depth++;
                    break;
                case '}':
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        yield return s[start..(i + 1)];
                        start = -1;
                    }
                    break;
            }
        }
    }

    private static LlmJsonPayload? TrySalvageFromText(string s)
    {
        var scoreMatch = Regex.Match(s, "\"alignmentScore\"\\s*:\\s*(\\d{1,3})", RegexOptions.IgnoreCase);
        int? score = null;
        if (scoreMatch.Success && int.TryParse(scoreMatch.Groups[1].Value, out var parsedScore))
        {
            score = Math.Clamp(parsedScore, 0, 100);
        }

        string? summary = null;
        var summaryMatch = Regex.Match(s, "\"summary\"\\s*:\\s*\"((?:\\\\\"|[^\"])*)\"", RegexOptions.IgnoreCase);
        if (summaryMatch.Success)
        {
            summary = NormalizeSummary(summaryMatch.Groups[1].Value
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t"));
        }

        if (score is null && summary is null)
        {
            return null;
        }

        return new LlmJsonPayload
        {
            AlignmentScore = score,
            Summary = summary,
            Flags = null
        };
    }

    private static string SelectParseInput(string content, string reasoning)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            return content.Trim();
        }

        return string.IsNullOrWhiteSpace(reasoning) ? string.Empty : reasoning.Trim();
    }

    private static double ClampSampling(double temperature) => Math.Clamp(temperature, 0, 2);

    private static double ClampTopP(double topP) => Math.Clamp(topP, 0.01, 1);

    private static bool IsCorpusWeakForFactCheck(IReadOnlyList<OfficialPublication> corpus) =>
        corpus.Count == 0;

    /// <summary>Согласует числовую оценку с флагами и негативными формулировками в summary (модель иногда завышает балл).</summary>
    private static int CalibrateAlignmentScore(int score, List<string>? flags, string? summary)
    {
        var calibrated = score;

        if (flags is not null)
        {
            var rejectsNews = flags.Any(f => string.Equals(f, "unsupported_claims", StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(f, "possible_contradiction", StringComparison.OrdinalIgnoreCase));
            if (rejectsNews)
            {
                calibrated = Math.Min(calibrated, 28);
            }
            else if (flags.Any(f => string.Equals(f, "accurate", StringComparison.OrdinalIgnoreCase)))
            {
                calibrated = Math.Max(calibrated, 68);
            }
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            var lower = summary.ToLowerInvariant();
            string[] strongRejection =
            [
                "конспиролог", "дезинформа", "миф", "несостоятельн", "недостоверн", "ложн",
                "вводит в заблуждение", "не подкреплен", "противоречат", "опроверг", "антинауч"
            ];

            var rejectionHits = strongRejection.Count(m => lower.Contains(m, StringComparison.Ordinal));
            calibrated = rejectionHits switch
            {
                >= 2 => Math.Min(calibrated, 18),
                1 => Math.Min(calibrated, 30),
                _ => calibrated
            };
        }

        return Math.Clamp(calibrated, 0, 100);
    }

    private static string? EnsureSubstantiveSummary(
        string? summary,
        List<string>? flags,
        int? score,
        bool corpusIsWeak)
    {
        var weakCorpus = corpusIsWeak ||
                          flags?.Any(f => string.Equals(f, "insufficient_corpus", StringComparison.OrdinalIgnoreCase)) == true;

        if (string.IsNullOrWhiteSpace(summary))
        {
            return score.HasValue ? BuildFallbackSummary(score.Value, weakCorpus) : null;
        }

        if (!weakCorpus && !LooksLikeCorpusOnlyRefusal(summary))
        {
            return summary;
        }

        if (HasMedicalKnowledgeAssessment(summary))
        {
            return summary;
        }

        var tail = BuildMedicalKnowledgeTail(score, weakCorpus);
        var trimmed = summary.TrimEnd();
        if (trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?'))
        {
            return $"{trimmed} {tail}";
        }

        return $"{trimmed}. {tail}";
    }

    private static string BuildFallbackSummary(int score, bool corpusIsWeak)
    {
        var corpusPart = corpusIsWeak
            ? "В доверенном корпусе нет выдержек, напрямую относящихся к теме новости."
            : "Сопоставление с корпусом дало ограниченный результат.";

        return $"{corpusPart} {BuildMedicalKnowledgeTail(score, corpusIsWeak)}";
    }

    private static string BuildMedicalKnowledgeTail(int? score, bool corpusIsWeak)
    {
        var prefix = corpusIsWeak
            ? "С учётом общих медицинских знаний (без опоры на корпус)"
            : "С учётом общих медицинских знаний";

        if (!score.HasValue)
        {
            return $"{prefix} содержание новости выглядит правдоподобным, но отдельные факты требуют проверки по официальным источникам.";
        }

        return score.Value switch
        {
            >= ReliabilityThresholds.ReliableMin =>
                $"{prefix} основные рекомендации в новости выглядят правдоподобными и не противоречат типичной практике, однако без прямых подтверждений из корпуса вывод остаётся предварительным.",
            <= ReliabilityThresholds.SuspiciousMax =>
                $"{prefix} в новости есть формулировки, которые могут вводить в заблуждение или противоречат медицинскому консенсусу; уверенность в достоверности низкая.",
            _ =>
                $"{prefix} часть тезисов звучит разумно, но конкретные цифры, региональная статистика и сильные утверждения нужно сверить с официальными публикациями."
        };
    }

    private static bool HasMedicalKnowledgeAssessment(string summary)
    {
        var lower = summary.ToLowerInvariant();
        string[] markers =
        [
            "общих медицин",
            "общепринят",
            "с учётом",
            "по общим",
            "правдоподоб",
            "рекомендац",
            "предварительн",
            "вероятн",
            "скорее всего",
            "можно предположить",
            "типичн",
            "не противореч"
        ];

        return markers.Any(m => lower.Contains(m, StringComparison.Ordinal));
    }

    private static bool LooksLikeCorpusOnlyRefusal(string summary)
    {
        if (CorpusOnlyRefusalRegex().IsMatch(summary))
        {
            return true;
        }

        var lower = summary.ToLowerInvariant();
        var hasRefusal = lower.Contains("невозможно проверить", StringComparison.Ordinal)
                         || lower.Contains("не удалось проверить", StringComparison.Ordinal)
                         || lower.Contains("проверить факты невозможно", StringComparison.Ordinal)
                         || lower.Contains("информации нет", StringComparison.Ordinal)
                         || lower.Contains("информации не найдено", StringComparison.Ordinal);

        return hasRefusal && !HasMedicalKnowledgeAssessment(summary);
    }

    private static string? NormalizeSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var s = summary.Trim();
        return IsTemplatePlaceholderSummary(s) ? null : s;
    }

    private static bool IsTemplatePlaceholderSummary(string summary)
    {
        if (TemplatePlaceholderRegex().IsMatch(summary))
        {
            return true;
        }

        var lower = summary.ToLowerInvariant();
        return lower.Contains("brief conclusion", StringComparison.Ordinal)
               || (lower.Contains('<') && lower.Contains("кратк") && lower.Contains("вывод"))
               || lower is "summary" or "вывод" or "snake_case";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    [GeneratedRegex(
        @"(невозможно\s+проверить|не\s+удалось\s+проверить|проверить\s+факт\w*\s+невозможно|информаци\w*\s+не\s+найден\w*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CorpusOnlyRefusalRegex();

    [GeneratedRegex(@"^<[^>]{1,120}>$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplatePlaceholderRegex();

    [GeneratedRegex(@"^```(?:json)?\s*|\s*```$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex MarkdownFenceRegex();

    private sealed class LlmJsonPayload
    {
        public int? AlignmentScore { get; set; }
        public string? Summary { get; set; }
        public List<string>? Flags { get; set; }
    }

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("top_p")]
        public double? TopP { get; set; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("response_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ResponseFormat? ResponseFormat { get; set; }

        [JsonPropertyName("think")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Think { get; set; }
    }

    private sealed class ResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "json_object";
    }

    private sealed class NativeChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public List<NativeChatMessage> Messages { get; set; } = [];

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("think")]
        public bool Think { get; set; }

        [JsonPropertyName("options")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public NativeChatOptions? Options { get; set; }
    }

    private sealed class NativeChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private sealed class NativeChatOptions
    {
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("top_p")]
        public double? TopP { get; set; }

        [JsonPropertyName("num_predict")]
        public int? NumPredict { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }
}
