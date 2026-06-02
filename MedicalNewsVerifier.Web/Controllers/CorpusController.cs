using MedicalNewsVerifier.Web;
using MedicalNewsVerifier.Web.Data;
using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Controllers;

/// <summary>Ручное наполнение корпуса текстов для сопоставления (MVP без авторизации).</summary>
public class CorpusController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new CorpusPageViewModel { Items = await LoadRecentPublicationsAsync(cancellationToken) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add([Bind(Prefix = "Input")] OfficialPublicationInputModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", new CorpusPageViewModel
            {
                Items = await LoadRecentPublicationsAsync(cancellationToken),
                Input = input
            });
        }

        var sourceName = input.SourceName.Trim();
        var source = await db.TrustedSources
            .FirstOrDefaultAsync(s => s.Name == sourceName, cancellationToken)
            ?? new TrustedSource { Name = sourceName, BaseUrl = input.Url.Trim(), IsEnabled = true };

        db.OfficialPublications.Add(new OfficialPublication
        {
            TrustedSource = source,
            Title = input.Title.Trim(),
            Url = input.Url.Trim(),
            Content = input.Content.Trim(),
            PublishedAtUtc = input.PublishedAtUtc is { } pub
                ? DateTimeUtc.ToPostgresUtc(pub)
                : DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Материал добавлен в корпус.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([Bind(Prefix = "Edit")] OfficialPublicationEditInputModel input, CancellationToken cancellationToken)
    {
        var publication = await db.OfficialPublications
            .Include(p => p.TrustedSource)
            .FirstOrDefaultAsync(p => p.Id == input.Id, cancellationToken);

        if (publication is null)
        {
            TempData["Error"] = "Материал не найден.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            var list = await LoadRecentPublicationsAsync(cancellationToken);
            return View("Index", new CorpusPageViewModel { Items = list, Edit = input });
        }

        var sourceName = input.SourceName.Trim();
        var source = publication.TrustedSource;
        if (source is null || source.Name != sourceName)
        {
            source = await db.TrustedSources.FirstOrDefaultAsync(s => s.Name == sourceName, cancellationToken)
                ?? new TrustedSource { Name = sourceName, BaseUrl = input.Url.Trim(), IsEnabled = true };
            publication.TrustedSource = source;
        }
        else if (string.IsNullOrWhiteSpace(source.BaseUrl))
        {
            source.BaseUrl = input.Url.Trim();
        }

        publication.Title = input.Title.Trim();
        publication.Url = input.Url.Trim();
        publication.Content = input.Content.Trim();
        publication.PublishedAtUtc = input.PublishedAtUtc is { } pub
            ? DateTimeUtc.ToPostgresUtc(pub)
            : publication.PublishedAtUtc;

        await db.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Материал обновлён.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var publication = await db.OfficialPublications.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (publication is null)
        {
            TempData["Error"] = "Материал не найден.";
            return RedirectToAction(nameof(Index));
        }

        if (await db.OfficialPublicationMatches.AnyAsync(m => m.OfficialPublicationId == id, cancellationToken))
        {
            TempData["Error"] = "Нельзя удалить материал: он используется в сохранённых проверках.";
            return RedirectToAction(nameof(Index));
        }

        db.OfficialPublications.Remove(publication);
        await db.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Материал удалён из корпуса.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<OfficialPublication>> LoadRecentPublicationsAsync(CancellationToken cancellationToken) =>
        await db.OfficialPublications
            .Include(p => p.TrustedSource)
            .AsNoTracking()
            .OrderByDescending(p => p.PublishedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
}
