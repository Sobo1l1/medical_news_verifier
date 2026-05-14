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
            НЕ ИСПОЛЬЗУЙ РЕЖИМ THINKING/МЫШЛЕНИЯ. ОТВЕЧАЙ ПРЯМО И БЫСТРО.
            НЕ ДОБАВЛЯЙ НИКАКИЕ ТЕГИ <think>, <thinking>, ```think ИЛИ ПОДОБНЫЕ.
            
            Ты медицинский фактчекер. Сравни новость пользователя с выдержками из доверенных материалов.
            Оцени согласованность фактов и тональности с опорой только на переданные выдержки (не выдумывай новые источники).
            
            ОТВЕТЬ ТОЛЬКО JSON-ОБЪЕКТОМ БЕЗ КАКИХ-ЛИБО ДРУГИХ ТЕКСТОВ:
            {"alignmentScore": <целое 0-100>, "summary": "<кратко на русском>", "flags": ["<строка>", ...]}
            
            alignmentScore: 80-100 если утверждения в целом согласуются или нейтральны относительно выдержек;
            40-79 если есть пробелы, обобщения или слабая опора;
            0-39 при явных противоречиях или сильной сенсационности относительно выдержек.
            flags: короткие метки (например "possible_contradiction", "insufficient_corpus", "sensational_tone").
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
            TopP = 0.1,         // Минимальный выбор вариантов
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
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            logger.LogInformation("Ollama raw response (content field): Length={Length}, Content={Content}", content.Length, content);

            var parsed = ParseModelJson(content);
            if (parsed is null)
            {
                logger.LogWarning("Ollama: не удалось разобрать JSON. Полный ответ: {FullContent}", content);
                return new OllamaComparisonOutcome
                {
                    WasAttempted = true,
                    Succeeded = false,
                    ErrorMessage = $"Модель вернула ответ, который не удалось разобрать как JSON. Сырой ответ: {content}"
                };
            }

            var score = parsed.AlignmentScore;
            if (score.HasValue)
            {
                score = Math.Clamp(score.Value, 0, 100);
            }

            return new OllamaComparisonOutcome
            {
                WasAttempted = true,
                Succeeded = true,
                AlignmentScore = score,
                Summary = string.IsNullOrWhiteSpace(parsed.Summary) ? null : parsed.Summary.Trim()
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

        // Удаляем markdown блоки
        trimmed = MarkdownFenceRegex().Replace(trimmed, "").Trim();

        // Удаляем thinking блоки (разные варианты)
        trimmed = Regex.Replace(trimmed, @"<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"<thinking>.*?</thinking>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"```think.*?```", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"^.*?(?=\{)", "", RegexOptions.Singleline); // Удаляем всё до первой {

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        var json = trimmed[start..(end + 1)];
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
