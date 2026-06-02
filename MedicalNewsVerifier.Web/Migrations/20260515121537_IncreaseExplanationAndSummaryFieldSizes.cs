using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalNewsVerifier.Web.Migrations;

/// <inheritdoc />
public partial class IncreaseExplanationAndSummaryFieldSizes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "AnalysisRecords" (
                "Id" serial PRIMARY KEY,
                "Headline" character varying(500) NOT NULL DEFAULT '',
                "NewsText" character varying(15000) NOT NULL DEFAULT '',
                "SourceUrl" character varying(500) NULL,
                "ReliabilityScore" integer NOT NULL DEFAULT 0,
                "Explanation" character varying(8000) NOT NULL DEFAULT '',
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
            );

            ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "HeuristicReliabilityScore" integer NOT NULL DEFAULT 0;
            ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "LlmAlignmentScore" integer NULL;
            ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "LlmSummary" character varying(8000) NULL;
            ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "Status" integer NOT NULL DEFAULT 0;
            ALTER TABLE "AnalysisRecords" ALTER COLUMN "NewsText" TYPE character varying(15000);
            ALTER TABLE "AnalysisRecords" ALTER COLUMN "Explanation" TYPE character varying(8000);
            ALTER TABLE "AnalysisRecords" ALTER COLUMN "LlmSummary" TYPE character varying(8000);

            CREATE TABLE IF NOT EXISTS "OfficialPublications" (
                "Id" serial PRIMARY KEY,
                "Title" character varying(500) NOT NULL DEFAULT '',
                "Content" character varying(5000) NOT NULL DEFAULT '',
                "Url" character varying(500) NOT NULL DEFAULT '',
                "PublishedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
            );
            ALTER TABLE "OfficialPublications" ADD COLUMN IF NOT EXISTS "SourceName" character varying(250) NOT NULL DEFAULT '';

            CREATE TABLE IF NOT EXISTS "TrustedSources" (
                "Id" serial PRIMARY KEY,
                "Name" character varying(300) NOT NULL,
                "BaseUrl" character varying(600) NOT NULL DEFAULT '',
                "AccessedOnUtc" timestamp with time zone NULL,
                "IsEnabled" boolean NOT NULL DEFAULT TRUE,
                "SortOrder" integer NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS "SuspiciousFragments" (
                "Id" serial PRIMARY KEY,
                "FeatureKind" integer NOT NULL DEFAULT 0,
                "StartOffset" integer NOT NULL DEFAULT -1,
                "EndOffset" integer NOT NULL DEFAULT -1,
                "FragmentText" character varying(1000) NOT NULL DEFAULT '',
                "Reason" character varying(300) NOT NULL DEFAULT '',
                "Severity" integer NOT NULL DEFAULT 0,
                "AnalysisRecordId" integer NOT NULL
            );

            ALTER TABLE "SuspiciousFragments" ADD COLUMN IF NOT EXISTS "FeatureKind" integer NOT NULL DEFAULT 0;
            ALTER TABLE "SuspiciousFragments" ADD COLUMN IF NOT EXISTS "StartOffset" integer NOT NULL DEFAULT -1;
            ALTER TABLE "SuspiciousFragments" ADD COLUMN IF NOT EXISTS "EndOffset" integer NOT NULL DEFAULT -1;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conname = 'FK_SuspiciousFragments_AnalysisRecords_AnalysisRecordId'
                ) THEN
                    ALTER TABLE "SuspiciousFragments"
                        ADD CONSTRAINT "FK_SuspiciousFragments_AnalysisRecords_AnalysisRecordId"
                        FOREIGN KEY ("AnalysisRecordId") REFERENCES "AnalysisRecords" ("Id") ON DELETE CASCADE;
                END IF;
            END$$;

            CREATE INDEX IF NOT EXISTS "IX_SuspiciousFragments_AnalysisRecordId"
                ON "SuspiciousFragments" ("AnalysisRecordId");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OfficialPublications");
        migrationBuilder.DropTable(name: "SuspiciousFragments");
        migrationBuilder.DropTable(name: "TrustedSources");
        migrationBuilder.DropTable(name: "AnalysisRecords");
    }
}
