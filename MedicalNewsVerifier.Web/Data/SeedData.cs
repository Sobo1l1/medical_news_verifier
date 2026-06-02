using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Data;

public static class SeedData
{
    public static void Initialize(AppDbContext db)
    {
        SeedTrustedSources(db);
        SeedCorpusPublications(db);
    }

    private static void SeedCorpusPublications(AppDbContext db)
    {
        if (db.OfficialPublications.Any())
        {
            return;
        }

        EnsureCorpusSources(db, out var who, out var minzdrav, out var cdc);

        db.OfficialPublications.AddRange(
            new OfficialPublication
            {
                TrustedSource = who,
                Title = "WHO updates guidance on respiratory infections",
                Content = "The World Health Organization confirms that preventive measures include vaccination, hand hygiene and targeted diagnostics.",
                Url = "https://www.who.int/",
                PublishedAtUtc = DateTime.UtcNow.AddDays(-14)
            },
            new OfficialPublication
            {
                TrustedSource = minzdrav,
                Title = "Разъяснение по профилактике сезонных вирусных заболеваний",
                Content = "Официальная рекомендация содержит данные о вакцинации, наблюдении у врача и недопустимости самолечения.",
                Url = "https://minzdrav.gov.ru/",
                PublishedAtUtc = DateTime.UtcNow.AddDays(-10)
            },
            new OfficialPublication
            {
                TrustedSource = cdc,
                Title = "Evidence update on vaccine safety",
                Content = "CDC reports that severe side effects are rare and vaccination significantly reduces severe outcomes.",
                Url = "https://www.cdc.gov/",
                PublishedAtUtc = DateTime.UtcNow.AddDays(-20)
            });

        db.SaveChanges();
    }

    private static void EnsureCorpusSources(
        AppDbContext db,
        out TrustedSource who,
        out TrustedSource minzdrav,
        out TrustedSource cdc)
    {
        who = GetOrCreateSource(db, "WHO", "https://www.who.int/");
        minzdrav = GetOrCreateSource(db, "Минздрав РФ", "https://minzdrav.gov.ru/");
        cdc = GetOrCreateSource(db, "CDC", "https://www.cdc.gov/");
        db.SaveChanges();
    }

    private static void SeedTrustedSources(AppDbContext db)
    {
        if (db.TrustedSources.Any())
        {
            return;
        }

        var accessed = new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc);
        db.TrustedSources.AddRange(
            new TrustedSource
            {
                Name = "Министерство здравоохранения Российской Федерации",
                BaseUrl = "https://minzdrav.gov.ru/",
                AccessedOnUtc = accessed,
                SortOrder = 10,
                IsEnabled = true
            },
            new TrustedSource
            {
                Name = "Росздравнадзор — клинические рекомендации",
                BaseUrl = "https://roszdravnadzor.gov.ru/medactivities/statecontrol/clinical",
                AccessedOnUtc = accessed,
                SortOrder = 20,
                IsEnabled = true
            },
            new TrustedSource
            {
                Name = "Государственный реестр лекарственных средств (ГРЛС)",
                BaseUrl = "https://grls.rosminzdrav.ru/Default.aspx",
                AccessedOnUtc = accessed,
                SortOrder = 30,
                IsEnabled = true
            },
            new TrustedSource
            {
                Name = "Роспотребнадзор — рекомендации населению",
                BaseUrl =
                    "https://cgon.rospotrebnadzor.ru/naseleniyu/zdorovyy-obraz-zhizni/rekomendatsii-naseleniyu-v-period-podema-zabolevaemosti-grippom-i-orvi/",
                AccessedOnUtc = accessed,
                SortOrder = 40,
                IsEnabled = true
            },
            new TrustedSource
            {
                Name = "Перечень рецензируемых научных изданий (ВАК)",
                BaseUrl = "https://perechen.vak2.ed.gov.ru/list",
                AccessedOnUtc = accessed,
                SortOrder = 50,
                IsEnabled = true
            },
            new TrustedSource
            {
                Name = "РИНЦ / eLIBRARY.RU",
                BaseUrl = "https://elibrary.ru/project_risc.asp",
                AccessedOnUtc = accessed,
                SortOrder = 60,
                IsEnabled = true
            });

        db.SaveChanges();
    }

    private static TrustedSource GetOrCreateSource(AppDbContext db, string name, string baseUrl)
    {
        var existing = db.TrustedSources.FirstOrDefault(s => s.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var source = new TrustedSource
        {
            Name = name,
            BaseUrl = baseUrl,
            IsEnabled = true,
            SortOrder = 0
        };
        db.TrustedSources.Add(source);
        return source;
    }
}
