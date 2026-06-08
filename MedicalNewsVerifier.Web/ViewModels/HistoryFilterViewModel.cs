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
    public int TotalPages => TotalCount == 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;

    public int DisplayFrom => TotalCount == 0 ? 0 : (PageNumber - 1) * PageSize + 1;

    public int DisplayTo => TotalCount == 0 ? 0 : Math.Min(PageNumber * PageSize, TotalCount);

    // Статистика
    public int SuspiciousCount { get; set; }
    public int NeedsReviewCount { get; set; }
    public int LikelyReliableCount { get; set; }
    public double AverageScore { get; set; }

    public string PaginationQuery(int page)
    {
        var parts = new List<string> { $"page={page}" };
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            parts.Add($"search={Uri.EscapeDataString(SearchText)}");
        }

        if (StatusFilter.HasValue)
        {
            parts.Add($"status={(int)StatusFilter.Value}");
        }

        if (MinScore.HasValue)
        {
            parts.Add($"minScore={MinScore.Value}");
        }

        if (MaxScore.HasValue)
        {
            parts.Add($"maxScore={MaxScore.Value}");
        }

        if (DateFrom.HasValue)
        {
            parts.Add($"dateFrom={DateFrom.Value:yyyy-MM-dd}");
        }

        if (DateTo.HasValue)
        {
            parts.Add($"dateTo={DateTo.Value:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(SortBy))
        {
            parts.Add($"sortBy={Uri.EscapeDataString(SortBy)}");
        }

        return "?" + string.Join("&", parts);
    }
}
