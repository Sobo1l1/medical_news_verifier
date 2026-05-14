using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Data;

/// <summary>
/// Добавляет столбцы разметки к существующей БД (EnsureCreated не обновляет схему).
/// </summary>
public static class DatabaseSchemaPatcher
{
    public static void ApplyAll(AppDbContext db)
    {
        ApplySuspiciousFragmentMarkupColumns(db);
        ApplyAnalysisRecordNewsTextLength(db);
    }

    public static void ApplySuspiciousFragmentMarkupColumns(AppDbContext db)
    {
        if (db.Database.ProviderName is null ||
            !db.Database.ProviderName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            db.Database.ExecuteSqlRaw(
                """ALTER TABLE "SuspiciousFragments" ADD COLUMN IF NOT EXISTS "FeatureKind" integer NOT NULL DEFAULT 0;""");
            db.Database.ExecuteSqlRaw(
                """ALTER TABLE "SuspiciousFragments" ADD COLUMN IF NOT EXISTS "StartOffset" integer NOT NULL DEFAULT -1;""");
            db.Database.ExecuteSqlRaw(
                """ALTER TABLE "SuspiciousFragments" ADD COLUMN IF NOT EXISTS "EndOffset" integer NOT NULL DEFAULT -1;""");
        }
        catch
        {
            // Игнорируем: при отсутствии прав или нестандартной схеме приложение всё равно должно стартовать.
        }
    }

    /// <summary>Увеличивает лимит текста новости до 15 000 символов в PostgreSQL.</summary>
    public static void ApplyAnalysisRecordNewsTextLength(AppDbContext db)
    {
        if (db.Database.ProviderName is null ||
            !db.Database.ProviderName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            db.Database.ExecuteSqlRaw(
                """ALTER TABLE "AnalysisRecords" ALTER COLUMN "NewsText" TYPE character varying(15000);""");
        }
        catch
        {
            // Таблица ещё не создана, тип уже 15000 или нет прав — пропускаем.
        }
    }
}
