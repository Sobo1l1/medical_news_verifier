using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MedicalNewsVerifier.Web.Migrations
{
    /// <inheritdoc />
    public partial class IncreaseExplanationAndSummaryFieldSizes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Headline = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NewsText = table.Column<string>(type: "character varying(15000)", maxLength: 15000, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReliabilityScore = table.Column<int>(type: "integer", nullable: false),
                    HeuristicReliabilityScore = table.Column<int>(type: "integer", nullable: false),
                    LlmAlignmentScore = table.Column<int>(type: "integer", nullable: true),
                    LlmSummary = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfficialPublications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialPublications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustedSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    AccessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustedSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SuspiciousFragments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FeatureKind = table.Column<int>(type: "integer", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    FragmentText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    AnalysisRecordId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuspiciousFragments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuspiciousFragments_AnalysisRecords_AnalysisRecordId",
                        column: x => x.AnalysisRecordId,
                        principalTable: "AnalysisRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SuspiciousFragments_AnalysisRecordId",
                table: "SuspiciousFragments",
                column: "AnalysisRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfficialPublications");

            migrationBuilder.DropTable(
                name: "SuspiciousFragments");

            migrationBuilder.DropTable(
                name: "TrustedSources");

            migrationBuilder.DropTable(
                name: "AnalysisRecords");
        }
    }
}
