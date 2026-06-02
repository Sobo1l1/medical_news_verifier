using MedicalNewsVerifier.Web;
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
            AccessedOnUtc = DateTimeUtc.ToPostgresUtc(input.AccessedOnUtc),
            IsEnabled = input.IsEnabled,
            SortOrder = maxOrder + 10
        });

        await db.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Источник добавлен.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([Bind(Prefix = "Edit")] TrustedSourceEditInputModel input, CancellationToken cancellationToken)
    {
        var source = await db.TrustedSources.FirstOrDefaultAsync(s => s.Id == input.Id, cancellationToken);
        if (source is null)
        {
            TempData["Error"] = "Источник не найден.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            var list = await db.TrustedSources.AsNoTracking()
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(cancellationToken);
            return View("Index", new SourcesPageViewModel { Items = list, Edit = input });
        }

        source.Name = input.Name.Trim();
        source.BaseUrl = input.BaseUrl.Trim();
        source.AccessedOnUtc = DateTimeUtc.ToPostgresUtc(input.AccessedOnUtc);
        source.IsEnabled = input.IsEnabled;
        source.SortOrder = input.SortOrder;

        await db.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Источник обновлён.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEnabled(int id, bool enabled, CancellationToken cancellationToken)
    {
        var source = await db.TrustedSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null)
        {
            TempData["Error"] = "Источник не найден.";
            return RedirectToAction(nameof(Index));
        }

        source.IsEnabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
        TempData["Message"] = enabled ? "Источник включён." : "Источник отключён.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var source = await db.TrustedSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null)
        {
            TempData["Error"] = "Источник не найден.";
            return RedirectToAction(nameof(Index));
        }

        if (await db.OfficialPublications.AnyAsync(p => p.TrustedSourceId == id, cancellationToken))
        {
            TempData["Error"] = "Нельзя удалить источник: в корпусе есть связанные материалы. Сначала удалите их.";
            return RedirectToAction(nameof(Index));
        }

        db.TrustedSources.Remove(source);
        await db.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Источник удалён.";
        return RedirectToAction(nameof(Index));
    }
}
