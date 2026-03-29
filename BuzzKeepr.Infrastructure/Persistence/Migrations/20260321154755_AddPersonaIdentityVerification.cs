using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonaIdentityVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentityVerificationStatus",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.AddColumn<string>(
                name: "PersonaInquiryId",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonaInquiryStatus",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PersonaVerifiedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedAddressCity",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedAddressPostalCode",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedAddressStreet1",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedAddressStreet2",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedAddressSubdivision",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedBirthdate",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedCountryCode",
                table: "Users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedFirstName",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedLastName",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedLicenseExpirationDate",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedLicenseLast4",
                table: "Users",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PersonaInquiryId",
                table: "Users",
                column: "PersonaInquiryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_PersonaInquiryId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IdentityVerificationStatus",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PersonaInquiryId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PersonaInquiryStatus",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PersonaVerifiedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedAddressCity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedAddressPostalCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedAddressStreet1",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedAddressStreet2",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedAddressSubdivision",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedBirthdate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedCountryCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedFirstName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedLastName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedLicenseExpirationDate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedLicenseLast4",
                table: "Users");
        }
    }
}
