using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeVerificationTokenHashPartial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VerificationTokens_TokenHash",
                table: "VerificationTokens");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationTokens_TokenHash",
                table: "VerificationTokens",
                column: "TokenHash",
                unique: true,
                filter: "\"ConsumedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VerificationTokens_TokenHash",
                table: "VerificationTokens");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationTokens_TokenHash",
                table: "VerificationTokens",
                column: "TokenHash",
                unique: true);
        }
    }
}
