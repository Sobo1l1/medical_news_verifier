using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Net;
using System.Text.Json;

namespace MedicalNewsVerifier.Web.Services;

public interface IAnalysisReportExporter
{
    byte[] GeneratePdf(AnalysisRecord record, IReadOnlyList<OfficialPublicationMatchVm>? matches = null);
    byte[] GenerateJson(AnalysisRecord record, IReadOnlyList<OfficialPublicationMatchVm>? matches = null);
}

public class AnalysisReportExporter : IAnalysisReportExporter
{
    static AnalysisReportExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly ILogger<AnalysisReportExporter> _logger;

    public AnalysisReportExporter(ILogger<AnalysisReportExporter> logger)
    {
        _logger = logger;
    }

    public byte[] GeneratePdf(AnalysisRecord record, IReadOnlyList<OfficialPublicationMatchVm>? matches = null)
    {
        try
        {
            matches ??= [];
            var statusText = ReliabilityThresholds.StatusLabel(record.ReliabilityScore);

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("Отчёт о проверке медицинской новости").Bold().FontSize(16);
                        col.Item().Text("MedNews Verifier").FontColor(Colors.Grey.Medium);
                    });

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().Text($"Заголовок: {record.Headline}").Bold();
                        col.Item().Text($"Статус: {statusText}");
                        col.Item().Text($"Эвристика: {record.HeuristicReliabilityScore}/100");
                        if (record.LlmAlignmentScore.HasValue)
                        {
                            col.Item().Text($"Ollama (согласованность с корпусом): {record.LlmAlignmentScore}/100");
                        }

                        col.Item().Text($"Итог: {record.ReliabilityScore}/100").Bold();

                        if (!string.IsNullOrWhiteSpace(record.Explanation))
                        {
                            col.Item().PaddingTop(8).Text("Анализ").Bold();
                            col.Item().Text(Truncate(record.Explanation, 1200));
                        }

                        if (!string.IsNullOrWhiteSpace(record.LlmSummary))
                        {
                            col.Item().PaddingTop(8).Text("Мнение модели (LLM)").Bold();
                            col.Item().Text(Truncate(record.LlmSummary, 600));
                        }

                        if (record.SuspiciousFragments.Count > 0)
                        {
                            col.Item().PaddingTop(8).Text($"Признаки ({record.SuspiciousFragments.Count})").Bold();
                            foreach (var fragment in record.SuspiciousFragments.Take(8))
                            {
                                col.Item().Text($"• {FeatureKindMetadata.Title((SuspiciousFeatureKind)fragment.FeatureKindId)}: {Truncate(fragment.Reason, 200)}");
                            }
                        }

                        if (matches.Count > 0)
                        {
                            col.Item().PaddingTop(8).Text("Официальные источники").Bold();
                            foreach (var match in matches.Take(10))
                            {
                                col.Item().Text($"• {match.SourceName}: {match.Title} ({match.RelevanceScore}%)");
                            }
                        }

                        col.Item().PaddingTop(12).Text($"Дата проверки: {record.CreatedAtUtc:dd.MM.yyyy HH:mm} UTC")
                            .FontColor(Colors.Grey.Medium).FontSize(9);
                    });
                });
            }).GeneratePdf();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при генерации PDF отчёта для анализа {RecordId}", record.Id);
            throw;
        }
    }

    public byte[] GenerateJson(AnalysisRecord record, IReadOnlyList<OfficialPublicationMatchVm>? matches = null)
    {
        try
        {
            matches ??= [];
            var json = new
            {
                id = record.Id,
                headline = record.Headline,
                newsText = record.NewsText,
                sourceUrl = record.SourceUrl,
                status = record.Status.ToString(),
                statusLabel = ReliabilityThresholds.StatusLabel(record.ReliabilityScore),
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
                officialMatches = matches.Select(m => new
                {
                    m.SourceName,
                    m.Title,
                    m.Url,
                    m.RelevanceScore,
                    m.HasStatistics
                }).ToList(),
                createdAtUtc = record.CreatedAtUtc
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.SerializeToUtf8Bytes(json, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при генерации JSON отчёта для анализа {RecordId}", record.Id);
            throw;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
