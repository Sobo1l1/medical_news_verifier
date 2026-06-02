using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Data;

/// <summary>
/// Ранее применял инкрементальные изменения к БД, созданным через EnsureCreated.
/// Схема теперь управляется EF-миграциями (<see cref="Program"/> → Database.Migrate).
/// </summary>
public static class DatabaseSchemaPatcher
{
    public static void ApplyAll(AppDbContext db)
    {
        // Намеренно пусто: все изменения — в Migrations/NormalizeTo3NF.
        _ = db.Database.ProviderName;
    }
}
