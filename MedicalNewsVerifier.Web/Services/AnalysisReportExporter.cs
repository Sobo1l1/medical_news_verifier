using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services;

public interface IAnalysisReportExporter
{
    byte[] GeneratePdf(AnalysisRecord record, string? sourceText = null);
    byte[] GenerateJson(AnalysisRecord record);
}

public class AnalysisReportExporter : IAnalysisReportExporter
{
    private readonly ILogger<AnalysisReportExporter> _logger;

    public AnalysisReportExporter(ILogger<AnalysisReportExporter> logger)
    {
        _logger = logger;
    }

    public byte[] GeneratePdf(AnalysisRecord record, string? sourceText = null)
    {
        try
        {
            // Генерируем HTML отчёт, который можно преобразовать в PDF
            var html = GenerateHtmlReport(record);
            
            // TODO: В будущем можно использовать HTML to PDF конвертер
            // Пока возвращаем HTML в качестве текста
            return System.Text.Encoding.UTF8.GetBytes(html);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при генерации PDF отчёта для анализа {RecordId}", record.Id);
            throw;
        }
    }

    public byte[] GenerateJson(AnalysisRecord record)
    {
        try
        {
            var json = new
            {
                id = record.Id,
                headline = record.Headline,
                newsText = record.NewsText,
                sourceUrl = record.SourceUrl,
                status = record.Status.ToString(),
                scores = new
                {
                    heuristic = record.HeuristicReliabilityScore,
                    llm = record.LlmAlignmentScore,
                    overall = record.ReliabilityScore
                },
                explanation = record.Explanation,
                llmSummary = record.LlmSummary,
                fragments = record.SuspiciousFragments.Select(f => new
                {
                    text = f.FragmentText,
                    kind = FeatureKindMetadata.Title((SuspiciousFeatureKind)f.FeatureKindId),
                    reason = f.Reason,
                    severity = f.Severity,
                    offset = new { start = f.StartOffset, end = f.EndOffset }
                }).ToList(),
                createdAtUtc = record.CreatedAtUtc
            };

            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var jsonString = System.Text.Json.JsonSerializer.Serialize(json, options);
            return System.Text.Encoding.UTF8.GetBytes(jsonString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при генерации JSON отчёта для анализа {RecordId}", record.Id);
            throw;
        }
    }

    private string GenerateHtmlReport(AnalysisRecord record)
    {
        var statusClass = record.Status switch
        {
            VerificationStatus.Suspicious => "danger",
            VerificationStatus.NeedsReview => "warning",
            _ => "success"
        };

        var statusText = record.Status switch
        {
            VerificationStatus.Suspicious => "ПОДОЗРИТЕЛЬНА",
            VerificationStatus.NeedsReview => "ТРЕБУЕТ ПРОВЕРКИ",
            _ => "ВЕРОЯТНО ДОСТОВЕРНА"
        };

        var html = $@"
<!DOCTYPE html>
<html lang='ru'>
<head>
    <meta charset='UTF-8'>
    <title>Отчёт о проверке</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; background-color: #f5f5f5; }}
        .header {{ text-align: center; margin-bottom: 30px; border-bottom: 2px solid #333; padding-bottom: 10px; }}
        .header h1 {{ margin: 0; color: #333; }}
        .content {{ background-color: white; padding: 20px; border-radius: 5px; box-shadow: 0 2px 5px rgba(0,0,0,0.1); }}
        .section {{ margin-bottom: 20px; }}
        .section h3 {{ color: #555; border-left: 4px solid #007bff; padding-left: 10px; margin-top: 0; }}
        .scores {{ background-color: #f9f9f9; padding: 10px; border-radius: 3px; }}
        .score-row {{ display: flex; justify-content: space-between; margin: 5px 0; }}
        .status-badge {{ padding: 10px; border-radius: 5px; background-color: #{{ statusClass }}; color: white; font-weight: bold; text-align: center; }}
        .fragment {{ background-color: #fff3cd; padding: 10px; margin: 5px 0; border-left: 3px solid #ff9800; }}
        .timestamp {{ color: #999; font-size: 12px; text-align: center; margin-top: 30px; border-top: 1px solid #ddd; padding-top: 10px; }}
    </style>
</head>
<body>
    <div class='header'>
        <h1>Отчёт о проверке медицинской новости</h1>
        <p>Система автоматической проверки достоверности</p>
    </div>

    <div class='content'>
        <div class='section'>
            <h3>Заголовок новости</h3>
            <p><strong>{record.Headline}</strong></p>
        </div>

        <div class='section'>
            <h3>Статус проверки</h3>
            <div class='status-badge'>{statusText}</div>
        </div>

        <div class='section'>
            <h3>Оценки</h3>
            <div class='scores'>
                <div class='score-row'>
                    <span>Эвристическая оценка:</span>
                    <strong>{record.HeuristicReliabilityScore}/100</strong>
                </div>";

        if (record.LlmAlignmentScore.HasValue)
        {
            html += $@"
                <div class='score-row'>
                    <span>Согласованность с корпусом (LLM):</span>
                    <strong>{record.LlmAlignmentScore}/100</strong>
                </div>";
        }

        html += $@"
                <div class='score-row' style='border-top: 1px solid #ddd; padding-top: 5px; margin-top: 5px;'>
                    <span><strong>Итоговая оценка:</strong></span>
                    <strong style='font-size: 16px;'>{record.ReliabilityScore}/100</strong>
                </div>
            </div>
        </div>";

        if (!string.IsNullOrWhiteSpace(record.Explanation))
        {
            var explanation = record.Explanation.Length > 800
                ? record.Explanation.Substring(0, 797) + "…"
                : record.Explanation;
            html += $@"
        <div class='section'>
            <h3>Анализ</h3>
            <p>{System.Web.HttpUtility.HtmlEncode(explanation)}</p>
        </div>";
        }

        if (!string.IsNullOrWhiteSpace(record.LlmSummary))
        {
            var summary = record.LlmSummary.Length > 400
                ? record.LlmSummary.Substring(0, 397) + "…"
                : record.LlmSummary;
            html += $@"
        <div class='section'>
            <h3>Мнение модели (LLM)</h3>
            <p>{System.Web.HttpUtility.HtmlEncode(summary)}</p>
        </div>";
        }

        if (record.SuspiciousFragments.Any())
        {
            html += $@"
        <div class='section'>
            <h3>Выявленные признаки ({record.SuspiciousFragments.Count})</h3>";
            
            var displayFragments = record.SuspiciousFragments.Take(8).ToList();
            foreach (var fragment in displayFragments)
            {
                html += $@"
            <div class='fragment'>
                <strong>{FeatureKindMetadata.Title((SuspiciousFeatureKind)fragment.FeatureKindId)}</strong><br/>
                <small>{System.Web.HttpUtility.HtmlEncode(fragment.Reason)}</small>
            </div>";
            }

            if (displayFragments.Count < record.SuspiciousFragments.Count)
            {
                html += $@"
            <p><em>...и ещё {record.SuspiciousFragments.Count - displayFragments.Count} признаков</em></p>";
            }

            html += @"
        </div>";
        }

        html += $@"
        <div class='timestamp'>
            Дата отчёта: {DateTime.UtcNow:dd.MM.yyyy HH:mm:ss UTC}
        </div>
    </div>
</body>
</html>";

        return html;
    }
}
