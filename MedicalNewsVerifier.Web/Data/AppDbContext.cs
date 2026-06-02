using MedicalNewsVerifier.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AnalysisRecord> AnalysisRecords => Set<AnalysisRecord>();
    public DbSet<NewsSubmission> NewsSubmissions => Set<NewsSubmission>();
    public DbSet<SuspiciousFragment> SuspiciousFragments => Set<SuspiciousFragment>();
    public DbSet<OfficialPublication> OfficialPublications => Set<OfficialPublication>();
    public DbSet<OfficialPublicationMatch> OfficialPublicationMatches => Set<OfficialPublicationMatch>();
    public DbSet<SuspiciousFeatureKindDefinition> SuspiciousFeatureKindDefinitions => Set<SuspiciousFeatureKindDefinition>();
    public DbSet<TrustedSource> TrustedSources => Set<TrustedSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NewsSubmission>()
            .HasIndex(s => s.ContentFingerprint)
            .IsUnique();

        modelBuilder.Entity<AnalysisRecord>()
            .HasOne(r => r.NewsSubmission)
            .WithMany(s => s.AnalysisRecords)
            .HasForeignKey(r => r.NewsSubmissionId)
            .OnDelete(DeleteBehavior.Restrict);

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
            .HasIndex(m => new { m.AnalysisRecordId, m.OfficialPublicationId })
            .IsUnique();

        modelBuilder.Entity<OfficialPublicationMatch>()
            .HasOne(m => m.OfficialPublication)
            .WithMany()
            .HasForeignKey(m => m.OfficialPublicationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OfficialPublication>()
            .HasOne(p => p.TrustedSource)
            .WithMany()
            .HasForeignKey(p => p.TrustedSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TrustedSource>()
            .HasIndex(s => s.Name)
            .IsUnique();

        modelBuilder.Entity<SuspiciousFragment>()
            .HasOne(f => f.FeatureKindDefinition)
            .WithMany()
            .HasForeignKey(f => f.FeatureKindId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SuspiciousFeatureKindDefinition>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasData(FeatureKindSeedData.Rows);
        });
    }
}
