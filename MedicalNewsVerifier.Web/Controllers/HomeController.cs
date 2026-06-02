using MedicalNewsVerifier.Web.Data;
using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.Services;
using MedicalNewsVerifier.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Controllers;

public class HomeController(ILogger<HomeController> logger, INewsAnalysisService analysisService, AppDbContext db) : Controller
{
    public IActionResult Index()
    {
        return View(new AnalyzeResultViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(AnalyzeNewsInputModel input, CancellationToken cancellationToken, bool forceNew = false)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", new AnalyzeResultViewModel { Input = input });
        }

        var (record, matches, isFromHistory) = await analysisService.AnalyzeAndSaveAsync(input, forceNew, cancellationToken);
        var vm = new AnalyzeResultViewModel
        {
            Input = input,
            LastAnalysis = record,
            OfficialMatches = matches,
            IsFromHistory = isFromHistory
        };
        PopulateMarkup(vm, record);
        return View("Index", vm);
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var record = await analysisService.GetAnalysisByIdAsync(id, cancellationToken);
        if (record is null)
        {
            return NotFound();
        }

        var matches = await analysisService.GetOfficialMatchesAsync(record.NewsText, cancellationToken);
        var vm = new AnalyzeResultViewModel
        {
            Input = new AnalyzeNewsInputModel
            {
                Headline = record.Headline,
                NewsText = record.NewsText,
                SourceUrl = record.SourceUrl
            },
            LastAnalysis = record,
            OfficialMatches = matches,
            IsFromHistory = true,
            OpenedFromHistory = true
        };
        PopulateMarkup(vm, record);
        return View("Index", vm);
    }

    public async Task<IActionResult> History(
        VerificationStatus? status,
        int? minScore,
        int? maxScore,
        DateTime? dateFrom,
        DateTime? dateTo,
        string? search,
        string? sortBy,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 20;

        var query = db.AnalysisRecords
            .Include(r => r.SuspiciousFragments)
            .AsQueryable();

        // Фильтрация по статусу
        if (status.HasValue)
        {
            query = query.Where(r =>
                (status.Value == VerificationStatus.Suspicious && r.ReliabilityScore <= 40) ||
                (status.Value == VerificationStatus.NeedsReview && r.ReliabilityScore > 40 && r.ReliabilityScore < 70) ||
                (status.Value == VerificationStatus.LikelyReliable && r.ReliabilityScore >= 70));
        }

        if (minScore.HasValue)
            query = query.Where(r => r.ReliabilityScore >= minScore.Value);

        if (maxScore.HasValue)
            query = query.Where(r => r.ReliabilityScore <= maxScore.Value);

        if (dateFrom.HasValue)
            query = query.Where(r => r.CreatedAtUtc >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(r => r.CreatedAtUtc < dateTo.Value.AddDays(1));

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r => r.Headline.Contains(search));

        // Подсчёт статистики (до пагинации)
        var totalCount = await query.CountAsync(cancellationToken);
        var suspiciousCount = await query.CountAsync(r => r.ReliabilityScore <= 40, cancellationToken);
        var needsReviewCount = await query.CountAsync(r => r.ReliabilityScore > 40 && r.ReliabilityScore < 70, cancellationToken);
        var reliableCount = await query.CountAsync(r => r.ReliabilityScore >= 70, cancellationToken);
        var avgScore = totalCount > 0 ? await query.AverageAsync(r => (double)r.ReliabilityScore, cancellationToken) : 0;

        // Сортировка
        query = sortBy switch
        {
            "DateAsc" => query.OrderBy(r => r.CreatedAtUtc),
            "ScoreDesc" => query.OrderByDescending(r => r.ReliabilityScore).ThenByDescending(r => r.CreatedAtUtc),
            "ScoreAsc" => query.OrderBy(r => r.ReliabilityScore).ThenByDescending(r => r.CreatedAtUtc),
            "HeadlineAsc" => query.OrderBy(r => r.Headline).ThenByDescending(r => r.CreatedAtUtc),
            _ => query.OrderByDescending(r => r.CreatedAtUtc)
        };

        // Пагинация
        var records = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var vm = new HistoryFilterViewModel
        {
            Records = records,
            TotalCount = totalCount,
            SuspiciousCount = suspiciousCount,
            NeedsReviewCount = needsReviewCount,
            LikelyReliableCount = reliableCount,
            AverageScore = avgScore,
            StatusFilter = status,
            MinScore = minScore,
            MaxScore = maxScore,
            DateFrom = dateFrom,
            DateTo = dateTo,
            SearchText = search,
            SortBy = sortBy ?? "DateDesc",
            PageNumber = page,
            PageSize = pageSize
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var record = await db.AnalysisRecords
            .Include(r => r.SuspiciousFragments)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (record is not null)
        {
            db.AnalysisRecords.Remove(record);
            await db.SaveChangesAsync(cancellationToken);
        }

        return RedirectToAction(nameof(History));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportPdf(int id, CancellationToken cancellationToken)
    {
        var record = await analysisService.GetAnalysisByIdAsync(id, cancellationToken);
        if (record is null)
            return NotFound();

        var exporter = HttpContext.RequestServices.GetRequiredService<IAnalysisReportExporter>();
        var pdf = exporter.GeneratePdf(record);
        
        return File(pdf, "application/pdf", $"report_{record.Id}_{DateTime.Now:yyyy-MM-dd_HHmm}.pdf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportJson(int id, CancellationToken cancellationToken)
    {
        try
        {
            var record = await analysisService.GetAnalysisByIdAsync(id, cancellationToken);
            if (record is null)
                return NotFound();

            // Простой JSON с основной информацией
            var simpleJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                id = record.Id,
                headline = record.Headline,
                status = record.Status.ToString(),
                overallScore = record.ReliabilityScore,
                heuristicScore = record.HeuristicReliabilityScore,
                llmScore = record.LlmAlignmentScore,
                explanation = record.Explanation,
                createdAtUtc = record.CreatedAtUtc,
                fragmentCount = record.SuspiciousFragments.Count
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(simpleJson);
            return File(jsonBytes, "application/json", $"report_{record.Id}_{DateTime.Now:yyyy-MM-dd_HHmm}.json");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error exporting JSON for record {Id}", id);
            return StatusCode(500, $"Error exporting JSON: {ex.Message}");
        }
    }

    private static void PopulateMarkup(AnalyzeResultViewModel vm, AnalysisRecord record)
    {
        var doc = AnalyzedDocument.From(record.Headline, record.NewsText);
        vm.MarkupSourceText = doc.FullText;
        vm.HighlightedHtml = TextMarkupBuilder.BuildStructuredDocumentHtml(doc, record.SuspiciousFragments.ToList());
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        logger.LogError("Unhandled MVC error");
        return View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
    }
}
