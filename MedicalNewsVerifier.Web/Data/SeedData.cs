using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Data;

public static class SeedData
{
    public static void Initialize(AppDbContext db)
    {
        if (db.OfficialPublications.Any())
        {
            return;
        }

        db.OfficialPublications.AddRange(
            new OfficialPublication
            {
                SourceName = "WHO",
                Title = "WHO updates guidance on respiratory infections",
                Content = "The World Health Organization confirms that preventive measures include vaccination, hand hygiene and targeted diagnostics.",
                Url = "https://www.who.int/",
                PublishedAtUtc = DateTime.UtcNow.AddDays(-14)
            },
            new OfficialPublication
            {
                SourceName = "Минздрав РФ",
                Title = "Разъяснение по профилактике сезонных вирусных заболеваний",
                Content = "Официальная рекомендация содержит данные о вакцинации, наблюдении у врача и недопустимости самолечения.",
                Url = "https://minzdrav.gov.ru/",
                PublishedAtUtc = DateTime.UtcNow.AddDays(-10)
            },
            new OfficialPublication
            {
                SourceName = "CDC",
                Title = "Evidence update on vaccine safety",
                Content = "CDC reports that severe side effects are rare and vaccination significantly reduces severe outcomes.",
                Url = "https://www.cdc.gov/",
                PublishedAtUtc = DateTime.UtcNow.AddDays(-20)
            });

        db.SaveChanges();
    }
}
