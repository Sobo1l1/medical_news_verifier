using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Data;

/// <summary>
/// БД, созданные через EnsureCreated без __EFMigrationsHistory, получают отметку о базовой миграции.
/// </summary>
public static class MigrationHistoryBaseline
{
    private const string InitialMigrationId = "20260515121537_IncreaseExplanationAndSummaryFieldSizes";

    public static void ApplyIfNeeded(AppDbContext db)
    {
        if (db.Database.ProviderName is null ||
            !db.Database.ProviderName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            db.Database.ExecuteSqlRaw(
                $"""
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                SELECT '{InitialMigrationId}', '9.0.4'
                WHERE EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'public' AND table_name = 'AnalysisRecords'
                )
                  AND NOT EXISTS (
                    SELECT 1 FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '{InitialMigrationId}'
                );
                """);
        }
        catch
        {
            // Таблица истории ещё не создана — Migrate() создаст её.
        }
    }
}
