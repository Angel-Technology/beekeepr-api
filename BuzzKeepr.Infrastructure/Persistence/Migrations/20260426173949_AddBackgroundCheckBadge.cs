using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackgroundCheckBadge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundCheckBadge",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTime>(
                name: "BackgroundCheckBadgeExpiresAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundCheckBadge",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BackgroundCheckBadgeExpiresAtUtc",
                table: "Users");
        }
    }
}
