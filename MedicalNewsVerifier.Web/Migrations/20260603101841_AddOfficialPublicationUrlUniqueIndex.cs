using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalNewsVerifier.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialPublicationUrlUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OfficialPublications_Url",
                table: "OfficialPublications",
                column: "Url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OfficialPublications_Url",
                table: "OfficialPublications");
        }
    }
}
