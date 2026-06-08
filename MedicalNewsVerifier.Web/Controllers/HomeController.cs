using MedicalNewsVerifier.Web.Data;
using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.Services;
using MedicalNewsVerifier.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Controllers;

public class HomeController(
    ILogger<HomeController> logger,
    INewsAnalysisService analysisService,
    IAnalysisReportExporter reportExporter,
    ISystemDiagnosticsService diagnosticsService,
    AppDbContext db,
    IConfiguration configuration) : Controller
{
    public IActionResult Index()
    {
        return View(new AnalyzeResultViewModel { OllamaEnabled = configuration.GetValue("Ollama:Enabled", false) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(AnalyzeNewsInputModel input, CancellationToken cancellationToken, bool forceNew = false)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", new AnalyzeResultViewModel { Input = input, OllamaEnabled = configuration.GetValue("Ollama:Enabled", false) });
        }

        var (record, matches, isFromHistory) = await analysisService.AnalyzeAndSaveAsync(input, forceNew, cancellationToken);
        var vm = new AnalyzeResultViewModel
        {
            Input = input,
            LastAnalysis = record,
            OfficialMatches = matches,
            IsFromHistory = isFromHistory,
            OllamaEnabled = configuration.GetValue("Ollama:Enabled", false)
        };
        PopulateMarkup(vm, record);
        return View("Index", vm);
    }

    public async Task<IActionResult> Details(int id, bool fromHistory = false, CancellationToken cancellationToken = default)
    {
        var record = await analysisService.GetAnalysisByIdAsync(id, cancellationToken);
        if (record is null)
        {
            return NotFound();
        }

        var matches = await analysisService.GetMatchesForAnalysisAsync(record.Id, cancellationToken);
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
            OpenedFromHistory = fromHistory,
            OllamaEnabled = configuration.GetValue("Ollama:Enabled", false)
        };
        PopulateMarkup(vm, record);
        return View("Index", vm);
    }

    public async Task<IActionResult> ExportPdf(int id, CancellationToken cancellationToken)
    {
        var record = await LoadRecordForExportAsync(id, cancellationToken);
        if (record is null)
        {
            return NotFound();
        }

        var matches = await analysisService.GetMatchesForAnalysisAsync(id, cancellationToken);
        var bytes = reportExporter.GeneratePdf(record, matches);
        return File(bytes, "application/pdf", $"analysis-{id}.pdf");
    }

    public async Task<IActionResult> ExportJson(int id, CancellationToken cancellationToken)
    {
        var record = await LoadRecordForExportAsync(id, cancellationToken);
        if (record is null)
        {
            return NotFound();
        }

        var matches = await analysisService.GetMatchesForAnalysisAsync(id, cancellationToken);
        var bytes = reportExporter.GenerateJson(record, matches);
        return File(bytes, "application/json", $"analysis-{id}.json");
    }

    public async Task<IActionResult> Diagnostics(CancellationToken cancellationToken)
    {
        var vm = await diagnosticsService.CheckAsync(cancellationToken);
        return View(vm);
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
            .Include(r => r.NewsSubmission)
            .Include(r => r.SuspiciousFragments)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(r =>
                (status.Value == VerificationStatus.Suspicious && r.ReliabilityScore <= ReliabilityThresholds.SuspiciousMax) ||
                (status.Value == VerificationStatus.NeedsReview && r.ReliabilityScore > ReliabilityThresholds.SuspiciousMax && r.ReliabilityScore < ReliabilityThresholds.ReliableMin) ||
                (status.Value == VerificationStatus.LikelyReliable && r.ReliabilityScore >= ReliabilityThresholds.ReliableMin));
        }

        if (minScore.HasValue)
        {
            query = query.Where(r => r.ReliabilityScore >= minScore.Value);
        }

        if (maxScore.HasValue)
        {
            query = query.Where(r => r.ReliabilityScore <= maxScore.Value);
        }

        if (dateFrom.HasValue)
        {
            var fromUtc = DateTimeUtc.ToPostgresUtc(dateFrom.Value);
            query = query.Where(r => r.CreatedAtUtc >= fromUtc);
        }

        if (dateTo.HasValue)
        {
            var toExclusiveUtc = DateTimeUtc.ToPostgresUtc(dateTo.Value).AddDays(1);
            query = query.Where(r => r.CreatedAtUtc < toExclusiveUtc);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r => r.NewsSubmission!.Headline.Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var suspiciousCount = await query.CountAsync(r => r.ReliabilityScore <= ReliabilityThresholds.SuspiciousMax, cancellationToken);
        var needsReviewCount = await query.CountAsync(
            r => r.ReliabilityScore > ReliabilityThresholds.SuspiciousMax && r.ReliabilityScore < ReliabilityThresholds.ReliableMin,
            cancellationToken);
        var reliableCount = await query.CountAsync(r => r.ReliabilityScore >= ReliabilityThresholds.ReliableMin, cancellationToken);
        var avgScore = totalCount > 0 ? await query.AverageAsync(r => (double)r.ReliabilityScore, cancellationToken) : 0;

        query = sortBy switch
        {
            "DateAsc" => query.OrderBy(r => r.CreatedAtUtc),
            "ScoreDesc" => query.OrderByDescending(r => r.ReliabilityScore).ThenByDescending(r => r.CreatedAtUtc),
            "ScoreAsc" => query.OrderBy(r => r.ReliabilityScore).ThenByDescending(r => r.CreatedAtUtc),
            "HeadlineAsc" => query.OrderBy(r => r.NewsSubmission!.Headline).ThenByDescending(r => r.CreatedAtUtc),
            _ => query.OrderByDescending(r => r.CreatedAtUtc)
        };

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

    private async Task<AnalysisRecord?> LoadRecordForExportAsync(int id, CancellationToken cancellationToken) =>
        await db.AnalysisRecords
            .Include(r => r.NewsSubmission)
            .Include(r => r.SuspiciousFragments)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

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
