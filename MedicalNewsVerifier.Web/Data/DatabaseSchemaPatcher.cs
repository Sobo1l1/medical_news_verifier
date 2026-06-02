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
        ApplyOfficialSourcesTable(db);
        ApplyTrustedSourcesTable(db);
        ApplyAnalysisRecordLlmColumns(db);
    }

    public static void ApplyTrustedSourcesTable(AppDbContext db)
    {
        if (db.Database.ProviderName is null ||
            !db.Database.ProviderName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS "TrustedSources" (
                    "Id" serial PRIMARY KEY,
                    "Name" character varying(300) NOT NULL,
                    "BaseUrl" character varying(600) NOT NULL,
                    "AccessedOnUtc" timestamp with time zone NULL,
                    "IsEnabled" boolean NOT NULL DEFAULT TRUE,
                    "SortOrder" integer NOT NULL DEFAULT 0
                );
                """);
        }
        catch
        {
            // Игнорируем при отсутствии прав или нестандартной схеме.
        }
    }

    public static void ApplyAnalysisRecordLlmColumns(AppDbContext db)
    {
        if (db.Database.ProviderName is null ||
            !db.Database.ProviderName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            db.Database.ExecuteSqlRaw(
                """ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "HeuristicReliabilityScore" integer NOT NULL DEFAULT 0;""");
            db.Database.ExecuteSqlRaw(
                """ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "LlmAlignmentScore" integer NULL;""");
            db.Database.ExecuteSqlRaw(
                """ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "LlmSummary" character varying(4000) NULL;""");
        }
        catch
        {
            // Таблица ещё не создана или колонки уже есть с другим типом — пропускаем.
        }
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

    public static void ApplyOfficialSourcesTable(AppDbContext db)
    {
        if (db.Database.ProviderName is null ||
            !db.Database.ProviderName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS "OfficialSources" (
                    "Id" serial PRIMARY KEY,
                    "Name" character varying(300) NOT NULL,
                    "BaseUrl" character varying(600) NULL
                );
                """);
            db.Database.ExecuteSqlRaw(
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_OfficialSources_Name" ON "OfficialSources" ("Name");""");
            db.Database.ExecuteSqlRaw(
                """ALTER TABLE "OfficialPublications" ADD COLUMN IF NOT EXISTS "OfficialSourceId" integer NULL;""");
            db.Database.ExecuteSqlRaw(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'OfficialPublications'
                          AND column_name = 'SourceName'
                    ) THEN
                        INSERT INTO "OfficialSources" ("Name")
                        SELECT DISTINCT "SourceName" FROM "OfficialPublications"
                        WHERE "SourceName" IS NOT NULL AND "SourceName" <> ''
                          AND NOT EXISTS (
                              SELECT 1 FROM "OfficialSources"
                              WHERE "OfficialSources"."Name" = "OfficialPublications"."SourceName"
                          );

                        INSERT INTO "OfficialSources" ("Name")
                        SELECT 'Unknown'
                        WHERE EXISTS (
                            SELECT 1 FROM "OfficialPublications"
                            WHERE COALESCE(NULLIF("SourceName", ''), '') = ''
                        )
                          AND NOT EXISTS (
                              SELECT 1 FROM "OfficialSources"
                              WHERE "Name" = 'Unknown'
                          );

                        UPDATE "OfficialPublications" SET "OfficialSourceId" = s."Id"
                        FROM "OfficialSources" s
                        WHERE s."Name" = COALESCE(NULLIF("SourceName", ''), 'Unknown');

                        ALTER TABLE "OfficialPublications" DROP COLUMN IF EXISTS "SourceName";
                    END IF;
                END$$;
                """);
            db.Database.ExecuteSqlRaw(
                """ALTER TABLE "OfficialPublications" ALTER COLUMN "OfficialSourceId" SET NOT NULL;""");
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
