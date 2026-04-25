using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckrProfileTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CheckrLastCheckAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CheckrLastCheckHasPossibleMatches",
                table: "Users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckrLastCheckId",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckrProfileId",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_CheckrProfileId",
                table: "Users",
                column: "CheckrProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_CheckrProfileId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CheckrLastCheckAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CheckrLastCheckHasPossibleMatches",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CheckrLastCheckId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CheckrProfileId",
                table: "Users");
        }
    }
}
