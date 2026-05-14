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
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var list = await db.OfficialPublications
            .AsNoTracking()
            .OrderByDescending(p => p.PublishedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        return View(new CorpusPageViewModel { Items = list });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add([Bind(Prefix = "Input")] OfficialPublicationInputModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var list = await db.OfficialPublications.AsNoTracking()
                .OrderByDescending(p => p.PublishedAtUtc).Take(100).ToListAsync(cancellationToken);
            return View("Index", new CorpusPageViewModel { Items = list, Input = input });
        }

        db.OfficialPublications.Add(new OfficialPublication
        {
            SourceName = input.SourceName.Trim(),
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
}
