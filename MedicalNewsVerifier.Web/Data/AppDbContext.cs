using MedicalNewsVerifier.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AnalysisRecord> AnalysisRecords => Set<AnalysisRecord>();
    public DbSet<SuspiciousFragment> SuspiciousFragments => Set<SuspiciousFragment>();
    public DbSet<OfficialPublication> OfficialPublications => Set<OfficialPublication>();
    public DbSet<OfficialSource> OfficialSources => Set<OfficialSource>();
    public DbSet<OfficialPublicationMatch> OfficialPublicationMatches => Set<OfficialPublicationMatch>();
    public DbSet<SuspiciousFeatureKindDefinition> SuspiciousFeatureKindDefinitions => Set<SuspiciousFeatureKindDefinition>();
    public DbSet<TrustedSource> TrustedSources => Set<TrustedSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalysisRecord>()
            .HasMany(r => r.SuspiciousFragments)
            .WithOne(f => f.AnalysisRecord)
            .HasForeignKey(f => f.AnalysisRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AnalysisRecord>()
            .HasMany(r => r.OfficialPublicationMatches)
            .WithOne(m => m.AnalysisRecord)
            .HasForeignKey(m => m.AnalysisRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OfficialPublicationMatch>()
            .HasOne(m => m.OfficialPublication)
            .WithMany()
            .HasForeignKey(m => m.OfficialPublicationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OfficialPublication>()
            .HasOne(p => p.OfficialSource)
            .WithMany(s => s.Publications)
            .HasForeignKey(p => p.OfficialSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OfficialSource>()
            .HasIndex(s => s.Name)
            .IsUnique();

        modelBuilder.Entity<SuspiciousFragment>()
            .HasOne(f => f.FeatureKindDefinition)
            .WithMany()
            .HasForeignKey(f => f.FeatureKindId);
    }
}
