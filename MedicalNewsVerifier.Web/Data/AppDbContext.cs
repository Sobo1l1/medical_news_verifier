using MedicalNewsVerifier.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AnalysisRecord> AnalysisRecords => Set<AnalysisRecord>();
    public DbSet<SuspiciousFragment> SuspiciousFragments => Set<SuspiciousFragment>();
    public DbSet<OfficialPublication> OfficialPublications => Set<OfficialPublication>();
    public DbSet<TrustedSource> TrustedSources => Set<TrustedSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalysisRecord>()
            .HasMany(r => r.SuspiciousFragments)
            .WithOne(f => f.AnalysisRecord)
            .HasForeignKey(f => f.AnalysisRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
