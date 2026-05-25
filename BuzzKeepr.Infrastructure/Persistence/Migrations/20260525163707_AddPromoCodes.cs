using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromoCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntitlementId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Duration = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MaxRedemptions = table.Column<int>(type: "integer", nullable: true),
                    RedemptionsUsed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromoRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromoCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RedeemedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromoRedemptions_PromoCodes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "PromoCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromoRedemptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_Code",
                table: "PromoCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromoRedemptions_PromoCodeId_UserId",
                table: "PromoRedemptions",
                columns: new[] { "PromoCodeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromoRedemptions_UserId",
                table: "PromoRedemptions",
                column: "UserId");

            // Seed initial promo codes. Fixed Guids and CreatedAtUtc keep this migration
            // deterministic. Duration is stored as the enum string (HasConversion<string>).
            // The entitlement identifier mirrors what's configured in the RevenueCat dashboard
            // for this account ("Buzzkeepr Pro", literally with the space).
            var createdAtUtc = new DateTime(2026, 5, 25, 16, 37, 7, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "PromoCodes",
                columns: new[]
                {
                    "Id", "Code", "EntitlementId", "Duration", "MaxRedemptions",
                    "RedemptionsUsed", "ExpiresAtUtc", "IsActive", "CreatedAtUtc"
                },
                values: new object[,]
                {
                    {
                        new Guid("a1b2c3d4-0001-4001-8001-000000000001"),
                        "NEWBEE2026",
                        "Buzzkeepr Pro",
                        "Monthly",
                        500,
                        0,
                        new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                        true,
                        createdAtUtc
                    },
                    {
                        new Guid("a1b2c3d4-0001-4001-8001-000000000002"),
                        "BUZZIN3",
                        "Buzzkeepr Pro",
                        "ThreeMonth",
                        250,
                        0,
                        null,
                        true,
                        createdAtUtc
                    },
                    {
                        new Guid("a1b2c3d4-0001-4001-8001-000000000003"),
                        "QUEENBEE26",
                        "Buzzkeepr Pro",
                        "SixMonth",
                        100,
                        0,
                        null,
                        true,
                        createdAtUtc
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromoRedemptions");

            migrationBuilder.DropTable(
                name: "PromoCodes");
        }
    }
}
