using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyVerifiedIdentityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "VerifiedAddressStreet1", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedAddressStreet2", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedAddressCity", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedAddressSubdivision", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedAddressPostalCode", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedCountryCode", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedLicenseLast4", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedLicenseExpirationDate", table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "VerifiedMiddleName",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedLicenseState",
                table: "Users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PhoneNumber", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedLicenseState", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedMiddleName", table: "Users");

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
                name: "VerifiedAddressCity",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedAddressSubdivision",
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
                name: "VerifiedCountryCode",
                table: "Users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedLicenseLast4",
                table: "Users",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedLicenseExpirationDate",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
