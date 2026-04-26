using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevenueCatAppUserId",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionCurrentPeriodEndUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionEntitlement",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionProductId",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionStatus",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionStore",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionUpdatedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SubscriptionWillRenew",
                table: "Users",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RevenueCatAppUserId",
                table: "Users",
                column: "RevenueCatAppUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_RevenueCatAppUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RevenueCatAppUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionCurrentPeriodEndUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionEntitlement",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionProductId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionStatus",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionStore",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionUpdatedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionWillRenew",
                table: "Users");
        }
    }
}
