using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.ViewModels;

public class HistoryFilterViewModel
{
    // Фильтры
    public VerificationStatus? StatusFilter { get; set; }
    public int? MinScore { get; set; }
    public int? MaxScore { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? SearchText { get; set; }  // поиск в заголовке
    public string? SortBy { get; set; } = "DateDesc";  // DateDesc, DateAsc, ScoreDesc, ScoreAsc, HeadlineAsc

    // Пагинация
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    // Результаты
    public List<AnalysisRecord> Records { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;

    // Статистика
    public int SuspiciousCount { get; set; }
    public int NeedsReviewCount { get; set; }
    public int LikelyReliableCount { get; set; }
    public double AverageScore { get; set; }
}
