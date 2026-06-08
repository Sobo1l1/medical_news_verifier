using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.Services.Parsers;
using MedicalNewsVerifier.Web.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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
            var statusLabel = ReliabilityThresholds.StatusLabel(record.ReliabilityScore);
            var statusColor = StatusColor(record.ReliabilityScore);
            var llmSummary = record.LlmSummary ?? string.Empty;
            var corpusWeak = matches.Count == 0
                || llmSummary.Contains("insufficient_corpus", StringComparison.OrdinalIgnoreCase)
                || (record.LlmAlignmentScore.HasValue && record.LlmAlignmentScore < 25);
            var explanationLines = record.Explanation
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Text("MedNews Verifier").FontSize(9).FontColor(Colors.Grey.Medium);
                        col.Item().Text("Результат проверки медицинской новости").Bold().FontSize(18);

                        col.Item().BorderLeft(4).BorderColor(statusColor).Padding(12).Column(card =>
                        {
                            card.Spacing(8);
                            card.Item().Row(row =>
                            {
                                row.Spacing(8);
                                row.AutoItem().Background(statusColor).PaddingHorizontal(10).PaddingVertical(4)
                                    .Text(statusLabel).Bold().FontColor(Colors.White).FontSize(11);
                                row.AutoItem().AlignMiddle().Text($"Итог: {record.ReliabilityScore}/100").Bold().FontSize(16);
                            });

                            card.Item().Row(scores =>
                            {
                                scores.Spacing(8);
                                ScoreBox(scores, AnalysisUiLabels.FeatureAnalysis, $"{record.HeuristicReliabilityScore}/100");
                                ScoreBox(scores, AnalysisUiLabels.NeuralAnalysis,
                                    record.LlmAlignmentScore?.ToString() + "/100" ?? "—");
                                ScoreBox(scores, "Итог", $"{record.ReliabilityScore}/100");
                            });

                            if (corpusWeak)
                            {
                                card.Item().Background(Colors.Yellow.Lighten4).Padding(8).Text(text =>
                                {
                                    text.Span("Согласованность с корпусом ограничена. ").Bold();
                                    text.Span(matches.Count == 0
                                        ? "Релевантные официальные материалы не найдены."
                                        : "Найденные материалы слабо связаны с темой новости.");
                                });
                            }

                            if (matches.Count > 0)
                            {
                                card.Item().Text($"Источники, использованные при проверке ({matches.Count})").Bold().FontSize(10);
                                foreach (var m in matches.Take(8))
                                {
                                    card.Item().Text($"• [{m.RelevanceLabel}] {m.SourceName} — {m.Title} ({m.RelevanceScore}%)");
                                }
                            }
                        });

                        col.Item().Text("Подробный результат").Bold().FontSize(13);

                        if (!string.IsNullOrWhiteSpace(record.LlmSummary))
                        {
                            col.Item().Background("#E7F1FF").Padding(10).Column(block =>
                            {
                                block.Item().Text($"Краткое резюме ({AnalysisUiLabels.NeuralAnalysis})").Bold();
                                block.Item().Text(Truncate(record.LlmSummary, 900));
                            });
                        }

                        if (explanationLines.Count > 0)
                        {
                            col.Item().Text("Сводка автоматического анализа").Bold();
                            foreach (var line in explanationLines.Take(12))
                            {
                                col.Item().PaddingLeft(8).Text($"— {Truncate(line, 300)}");
                            }
                        }

                        if (record.SuspiciousFragments.Count > 0)
                        {
                            col.Item().PaddingTop(4).Text($"Выявленные признаки ({record.SuspiciousFragments.Count})").Bold();
                            foreach (var fragment in record.SuspiciousFragments.Take(10))
                            {
                                col.Item().Background(Colors.Yellow.Lighten5).BorderLeft(3).BorderColor(Colors.Orange.Medium)
                                    .Padding(6).Column(f =>
                                    {
                                        f.Item().Text(FeatureKindMetadata.Title((SuspiciousFeatureKind)fragment.FeatureKindId)).Bold();
                                        f.Item().Text(Truncate(fragment.Reason, 220)).FontSize(9);
                                    });
                            }
                        }

                        if (matches.Count > 0)
                        {
                            col.Item().PaddingTop(4).Text("Релевантные официальные публикации").Bold();
                            foreach (var m in matches)
                            {
                                col.Item().Row(row =>
                                {
                                    row.RelativeItem().Column(c =>
                                    {
                                        c.Item().Text($"{m.SourceName}: {m.Title}").Bold();
                                        c.Item().Text(m.Url).FontSize(8).FontColor(Colors.Blue.Medium);
                                    });
                                    row.ConstantItem(90).AlignRight()
                                        .Text($"{m.RelevanceLabel}\n{m.RelevanceScore}%").FontSize(9);
                                });
                            }
                        }

                        col.Item().PaddingTop(8).Text($"Дата проверки: {record.CreatedAtUtc:dd.MM.yyyy HH:mm} UTC")
                            .FontColor(Colors.Grey.Medium).FontSize(8);
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
                    featureAnalysis = record.HeuristicReliabilityScore,
                    neuralNetwork = record.LlmAlignmentScore,
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
                    m.RelevanceLabel
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

    private static void ScoreBox(RowDescriptor row, string label, string value)
    {
        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
        {
            c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
            c.Item().Text(value).Bold().FontSize(12);
        });
    }

    private static string StatusColor(int score) => score switch
    {
        >= ReliabilityThresholds.ReliableMin => Colors.Green.Medium,
        <= ReliabilityThresholds.SuspiciousMax => Colors.Red.Medium,
        _ => Colors.Orange.Medium
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
