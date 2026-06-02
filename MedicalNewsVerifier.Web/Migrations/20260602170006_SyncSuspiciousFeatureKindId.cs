using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalNewsVerifier.Web.Migrations;

/// <inheritdoc />
public partial class SyncSuspiciousFeatureKindId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Id уже без IDENTITY (создаётся в NormalizeTo3NF).
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
