using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.Services.Parsers;

namespace MedicalNewsVerifier.Web.ViewModels;

public class AnalyzeResultViewModel
{
    public AnalyzeNewsInputModel Input { get; set; } = new();
    public AnalysisRecord? LastAnalysis { get; set; }
    public List<OfficialPublicationMatchVm> OfficialMatches { get; set; } = [];
    public bool IsFromHistory { get; set; }

    /// <summary>Открыто из журнала проверок (GET Details?fromHistory=true).</summary>
    public bool OpenedFromHistory { get; set; }

    /// <summary>Строка, по которой заданы смещения фрагментов (заголовок + тело).</summary>
    public string? MarkupSourceText { get; set; }

    /// <summary>HTML с экранированием и тегами mark для подсветки.</summary>
    public string? HighlightedHtml { get; set; }

    public bool OllamaEnabled { get; set; }
}

public class OfficialPublicationMatchVm
{
    public int OfficialPublicationId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int RelevanceScore { get; set; }
    public bool HasStatistics { get; set; }

    public string RelevanceLabel => RelevanceScoring.RelevanceLabel(RelevanceScore);

    public string RelevanceBadgeClass => RelevanceScoring.RelevanceBadgeClass(RelevanceScore);
}
