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
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxCorpusCharsPerSnippet { get; set; } = 1200;
    public int MaxCorpusSnippets { get; set; } = 4;
    /// <summary>Максимальное число токенов в ответе от модели. По умолчанию ~2000 токенов ≈ 5-6 КБ текста.</summary>
    public int MaxResponseTokens { get; set; } = 2000;
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

        var corpusBlock = BuildCorpusBlock(corpusExcerpts, opt.MaxCorpusSnippets, opt.MaxCorpusCharsPerSnippet);
        var systemPrompt =
            """
            ТЫ ОБЯЗАН ВЕРНУТЬ СТРОГО ВАЛИДНЫЙ JSON И НИЧЕГО КРОМЕ JSON.

            ЗАПРЕЩЕНО:
            - любой текст вне JSON;
            - markdown;
            - комментарии;
            - пояснения;
            - reasoning/thinking;
            - служебные сообщения;
            - ```json блоки;
            - переносы с пояснениями.

            ЕСЛИ ДАННЫХ НЕДОСТАТОЧНО — ВСЁ РАВНО ВЕРНИ JSON ПО СХЕМЕ.

            Ты — медицинский фактчекер и аналитик достоверности.
            Твоя задача:
            1. Сравнить текст новости пользователя с предоставленными выдержками из доверенных источников.
            2. Оценивать ТОЛЬКО фактическое соответствие переданным выдержкам.
            3. НЕ додумывать факты.
            4. НЕ учитывать стиль написания как ложность, если факты не противоречат выдержкам.
            5. Если выдержки не покрывают часть утверждений — снижать уверенность, но не считать это автоматически ложью.
            6. Если новость содержит категоричные, сенсационные, манипулятивные или неподтверждённые формулировки — отражать это во flags.
            7. Если источники противоречат новости напрямую — существенно снижать alignmentScore.

            ТРЕБОВАНИЯ К ОЦЕНКЕ:
            - 80-100:
              Утверждения в целом подтверждаются выдержками либо не противоречат им.
            - 40-79:
              Есть слабая опора, неполное покрытие, преувеличения, неоднозначность или недостаток подтверждений.
            - 0-39:
              Есть явные противоречия, сильная сенсационность, искажение выводов или неподтверждённые категоричные утверждения.

            ТРЕБОВАНИЯ К summary:
            - Только русский язык.
            - Кратко.
            - 1-3 предложения.
            - Без markdown.
            - Описывать только вывод проверки.

            ТРЕБОВАНИЯ К flags:
            Используй короткие snake_case метки.
            Допустимые примеры:
            - "possible_contradiction"
            - "insufficient_corpus"
            - "sensational_tone"
            - "unsupported_claims"
            - "exaggeration"
            - "selective_framing"
            - "consistent_with_sources"
            - "neutral_tone"

            ЕСЛИ ФЛАГОВ НЕТ — ВЕРНИ ПУСТОЙ МАССИВ.

            СТРОГАЯ СХЕМА ОТВЕТА:
            {
              "alignmentScore": <integer 0-100>,
              "summary": "<string>",
              "flags": ["<string>"]
            }

            ВАЖНО:
            - alignmentScore должен быть ЦЕЛЫМ ЧИСЛОМ.
            - flags всегда массив.
            - summary всегда строка.
            - JSON должен корректно парситься стандартным JSON parser.
            - Не экранируй JSON в строку.
            - Не добавляй лишние поля.
            - Не добавляй trailing commas.
            - Верни только один JSON-объект.
            """;

        var userPrompt =
            $"""
            НОВОСТЬ ПОЛЬЗОВАТЕЛЯ:
            {userNews}

            ВЫДЕРЖКИ ИЗ ДОВЕРЕННОГО КОРПУСА:
         
            {corpusBlock}
            """;

        var payload = new ChatCompletionRequest
        {
            Model = opt.Model.Trim(),
            Stream = false,
            Temperature = 0.0,  // Полностью детерминированный, без thinking
            TopP = 0.1,
            MaxTokens = opt.MaxResponseTokens,  // Ограничиваем длину ответа
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userPrompt }
            ]
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama HTTP {Status}: {Body}", (int)response.StatusCode, Truncate(body, 500));
                return new OllamaComparisonOutcome
                {
                    WasAttempted = true,
                    Succeeded = false,
                    ErrorMessage = $"Ollama вернула код {(int)response.StatusCode}."
                };
            }

            logger.LogInformation("Ollama full response body: {Body}", body);

            using var doc = JsonDocument.Parse(body);
            var messageObj = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
            
            // Пытаемся получить content, если пуст — ищем reasoning
            var content = messageObj.GetProperty("content").GetString() ?? "";
            
            // Если content пуст, пробуем reasoning (некоторые модели туда вывалят ответ)
            if (string.IsNullOrWhiteSpace(content) && messageObj.TryGetProperty("reasoning", out var reasoningProp))
            {
                var reasoning = reasoningProp.GetString() ?? "";
                logger.LogWarning("Ollama: content пуст, используем reasoning поле (длина {Length})", reasoning.Length);
                content = reasoning;
            }
            
            logger.LogInformation("Ollama raw response (content field): Length={Length}, Content={Content}", content.Length, content.Length > 200 ? content[..200] : content);

            var parsed = ParseModelJson(content);
            if (parsed is null)
            {
                logger.LogWarning("Ollama: не удалось разобрать JSON. Длина: {Length} chars. Первые 500 символов: {Preview}",
                    content.Length,
                    content.Length > 500 ? content[..500] : content);
                return new OllamaComparisonOutcome
                {
                    WasAttempted = true,
                    Succeeded = false,
                    ErrorMessage = $"Модель вернула ответ, который не удалось разобрать как JSON (длина {content.Length}). Проверьте логи приложения."
                };
            }

            var score = parsed.AlignmentScore;
            if (score.HasValue)
            {
                score = Math.Clamp(score.Value, 0, 100);
            }

            var summary = parsed.Summary?.Trim();
            // Дополнительная подстраховка: обрезаем summary до 3500 символов на случай, если модель не соблюдала max_tokens
            if (summary?.Length > 3500)
            {
                summary = summary[..3497] + "…";
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

    private static string BuildCorpusBlock(IReadOnlyList<OfficialPublication> corpus, int maxSnippets, int maxChars)
    {
        if (corpus.Count == 0)
        {
            return "(Корпус пуст — оцени только внутреннюю согласованность и осторожность формулировок; в flags добавь insufficient_corpus.)";
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

        // Удаляем markdown блоки
        trimmed = MarkdownFenceRegex().Replace(trimmed, "").Trim();

        // Удаляем всё до первой {
        var jsonStart = trimmed.IndexOf('{');
        if (jsonStart < 0)
        {
            return null;
        }

        trimmed = trimmed[jsonStart..].Trim();

        // Находим последний }
        var jsonEnd = trimmed.LastIndexOf('}');
        if (jsonEnd <= 0)
        {
            return null;
        }

        var json = trimmed[..(jsonEnd + 1)];
        try
        {
            return JsonSerializer.Deserialize<LlmJsonPayload>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

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
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }
}
