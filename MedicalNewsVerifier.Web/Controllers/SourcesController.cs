using MedicalNewsVerifier.Web.Data;
using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Controllers;

/// <summary>Справочник доверенных URL (MVP без авторизации — не для публичного интернета).</summary>
public class SourcesController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var list = await db.TrustedSources
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return View(new SourcesPageViewModel { Items = list });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add([Bind(Prefix = "Input")] TrustedSourceInputModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var list = await db.TrustedSources.AsNoTracking()
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(cancellationToken);
            return View("Index", new SourcesPageViewModel { Items = list, Input = input });
        }

        var maxOrder = await db.TrustedSources.Select(s => (int?)s.SortOrder).MaxAsync(cancellationToken) ?? 0;
        db.TrustedSources.Add(new TrustedSource
        {
            Name = input.Name.Trim(),
            BaseUrl = input.BaseUrl.Trim(),
            AccessedOnUtc = input.AccessedOnUtc,
            IsEnabled = input.IsEnabled,
            SortOrder = maxOrder + 10
        });

        await db.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Источник добавлен.";
        return RedirectToAction(nameof(Index));
    }
}
