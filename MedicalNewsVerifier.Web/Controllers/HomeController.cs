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

    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        var records = await db.AnalysisRecords
            .Include(r => r.SuspiciousFragments)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        return View(records);
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
