using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitUserAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: create the four sub-aggregate tables. Id uses gen_random_uuid() as the
            // DB-side default so EnsureProfile/EnsureIdentityVerification/... can leave the key at
            // Guid.Empty in C# — EF Core's state-inference then treats nav-attached new rows as
            // Added rather than as existing-but-modified rows (which would generate an UPDATE
            // against nothing and throw a concurrency exception).
            migrationBuilder.CreateTable(
                name: "UserBackgroundChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckrProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CheckrLastCheckId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CheckrLastCheckAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckrLastCheckHasPossibleMatches = table.Column<bool>(type: "boolean", nullable: true),
                    Badge = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "None"),
                    BadgeExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBackgroundChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBackgroundChecks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserIdentityVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "NotStarted"),
                    PersonaInquiryId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PersonaInquiryStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PersonaInquiryUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedFirstName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VerifiedMiddleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VerifiedLastName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VerifiedBirthdate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    VerifiedLicenseState = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PersonaVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserIdentityVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserIdentityVerifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Nickname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Handle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "None"),
                    Entitlement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Store = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WillRenew = table.Column<bool>(type: "boolean", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevenueCatAppUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Step 2: backfill from the existing Users columns. Each INSERT is conditional —
            // we only create a sub-aggregate row when there's actual state to carry. Users who
            // never started Persona, never ran Checkr, never subscribed, and have no profile
            // fields filled stay with no sub-aggregates and the application code reads
            // null → default-enum-value naturally.
            migrationBuilder.Sql(@"
                INSERT INTO ""UserProfiles"" (""Id"", ""UserId"", ""DisplayName"", ""Nickname"", ""Handle"", ""ImageUrl"", ""PhoneNumber"", ""CreatedAtUtc"", ""UpdatedAtUtc"")
                SELECT gen_random_uuid(), ""Id"", ""DisplayName"", ""Nickname"", ""Handle"", ""ImageUrl"", ""PhoneNumber"", ""CreatedAtUtc"", NULL
                FROM ""Users""
                WHERE ""DisplayName"" IS NOT NULL
                   OR ""Nickname"" IS NOT NULL
                   OR ""Handle"" IS NOT NULL
                   OR ""ImageUrl"" IS NOT NULL
                   OR ""PhoneNumber"" IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""UserIdentityVerifications"" (""Id"", ""UserId"", ""Status"", ""PersonaInquiryId"", ""PersonaInquiryStatus"", ""PersonaInquiryUpdatedAtUtc"", ""VerifiedFirstName"", ""VerifiedMiddleName"", ""VerifiedLastName"", ""VerifiedBirthdate"", ""VerifiedLicenseState"", ""PersonaVerifiedAtUtc"")
                SELECT gen_random_uuid(), ""Id"", ""IdentityVerificationStatus"", ""PersonaInquiryId"", ""PersonaInquiryStatus"", ""PersonaInquiryUpdatedAtUtc"", ""VerifiedFirstName"", ""VerifiedMiddleName"", ""VerifiedLastName"", ""VerifiedBirthdate"", ""VerifiedLicenseState"", ""PersonaVerifiedAtUtc""
                FROM ""Users""
                WHERE ""IdentityVerificationStatus"" <> 'NotStarted'
                   OR ""PersonaInquiryId"" IS NOT NULL
                   OR ""PersonaInquiryStatus"" IS NOT NULL
                   OR ""VerifiedFirstName"" IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""UserBackgroundChecks"" (""Id"", ""UserId"", ""CheckrProfileId"", ""CheckrLastCheckId"", ""CheckrLastCheckAtUtc"", ""CheckrLastCheckHasPossibleMatches"", ""Badge"", ""BadgeExpiresAtUtc"")
                SELECT gen_random_uuid(), ""Id"", ""CheckrProfileId"", ""CheckrLastCheckId"", ""CheckrLastCheckAtUtc"", ""CheckrLastCheckHasPossibleMatches"", ""BackgroundCheckBadge"", ""BackgroundCheckBadgeExpiresAtUtc""
                FROM ""Users""
                WHERE ""BackgroundCheckBadge"" <> 'None'
                   OR ""CheckrProfileId"" IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""UserSubscriptions"" (""Id"", ""UserId"", ""Status"", ""Entitlement"", ""ProductId"", ""Store"", ""CurrentPeriodEndUtc"", ""WillRenew"", ""UpdatedAtUtc"", ""RevenueCatAppUserId"")
                SELECT gen_random_uuid(), ""Id"", ""SubscriptionStatus"", ""SubscriptionEntitlement"", ""SubscriptionProductId"", ""SubscriptionStore"", ""SubscriptionCurrentPeriodEndUtc"", ""SubscriptionWillRenew"", ""SubscriptionUpdatedAtUtc"", ""RevenueCatAppUserId""
                FROM ""Users""
                WHERE ""SubscriptionStatus"" <> 'None'
                   OR ""RevenueCatAppUserId"" IS NOT NULL;
            ");

            // Step 3: drop the now-redundant indexes and columns on Users. The unique indexes
            // (Handle / PersonaInquiryId / CheckrProfileId / RevenueCatAppUserId) move with their
            // data to the new tables.
            migrationBuilder.DropIndex(name: "IX_Users_CheckrProfileId", table: "Users");
            migrationBuilder.DropIndex(name: "IX_Users_Handle", table: "Users");
            migrationBuilder.DropIndex(name: "IX_Users_PersonaInquiryId", table: "Users");
            migrationBuilder.DropIndex(name: "IX_Users_RevenueCatAppUserId", table: "Users");

            migrationBuilder.DropColumn(name: "BackgroundCheckBadge", table: "Users");
            migrationBuilder.DropColumn(name: "BackgroundCheckBadgeExpiresAtUtc", table: "Users");
            migrationBuilder.DropColumn(name: "CheckrLastCheckAtUtc", table: "Users");
            migrationBuilder.DropColumn(name: "CheckrLastCheckHasPossibleMatches", table: "Users");
            migrationBuilder.DropColumn(name: "CheckrLastCheckId", table: "Users");
            migrationBuilder.DropColumn(name: "CheckrProfileId", table: "Users");
            migrationBuilder.DropColumn(name: "DisplayName", table: "Users");
            migrationBuilder.DropColumn(name: "Handle", table: "Users");
            migrationBuilder.DropColumn(name: "IdentityVerificationStatus", table: "Users");
            migrationBuilder.DropColumn(name: "ImageUrl", table: "Users");
            migrationBuilder.DropColumn(name: "Nickname", table: "Users");
            migrationBuilder.DropColumn(name: "PersonaInquiryId", table: "Users");
            migrationBuilder.DropColumn(name: "PersonaInquiryStatus", table: "Users");
            migrationBuilder.DropColumn(name: "PersonaInquiryUpdatedAtUtc", table: "Users");
            migrationBuilder.DropColumn(name: "PersonaVerifiedAtUtc", table: "Users");
            migrationBuilder.DropColumn(name: "PhoneNumber", table: "Users");
            migrationBuilder.DropColumn(name: "RevenueCatAppUserId", table: "Users");
            migrationBuilder.DropColumn(name: "SubscriptionCurrentPeriodEndUtc", table: "Users");
            migrationBuilder.DropColumn(name: "SubscriptionEntitlement", table: "Users");
            migrationBuilder.DropColumn(name: "SubscriptionProductId", table: "Users");
            migrationBuilder.DropColumn(name: "SubscriptionStatus", table: "Users");
            migrationBuilder.DropColumn(name: "SubscriptionStore", table: "Users");
            migrationBuilder.DropColumn(name: "SubscriptionUpdatedAtUtc", table: "Users");
            migrationBuilder.DropColumn(name: "SubscriptionWillRenew", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedBirthdate", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedFirstName", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedLastName", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedLicenseState", table: "Users");
            migrationBuilder.DropColumn(name: "VerifiedMiddleName", table: "Users");

            // Step 4: indexes on the new tables. Done last so the unique-Handle index is built
            // once against the already-backfilled data instead of in two passes.
            migrationBuilder.CreateIndex(
                name: "IX_UserBackgroundChecks_CheckrProfileId",
                table: "UserBackgroundChecks",
                column: "CheckrProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBackgroundChecks_UserId",
                table: "UserBackgroundChecks",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentityVerifications_PersonaInquiryId",
                table: "UserIdentityVerifications",
                column: "PersonaInquiryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentityVerifications_UserId",
                table: "UserIdentityVerifications",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_Handle",
                table: "UserProfiles",
                column: "Handle",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_UserId",
                table: "UserProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_RevenueCatAppUserId",
                table: "UserSubscriptions",
                column: "RevenueCatAppUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId",
                table: "UserSubscriptions",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add the dropped columns on Users.
            migrationBuilder.AddColumn<string>(name: "BackgroundCheckBadge", table: "Users", type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "None");
            migrationBuilder.AddColumn<DateTime>(name: "BackgroundCheckBadgeExpiresAtUtc", table: "Users", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "CheckrLastCheckAtUtc", table: "Users", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "CheckrLastCheckHasPossibleMatches", table: "Users", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<string>(name: "CheckrLastCheckId", table: "Users", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "CheckrProfileId", table: "Users", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "DisplayName", table: "Users", type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Handle", table: "Users", type: "character varying(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<string>(name: "IdentityVerificationStatus", table: "Users", type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "NotStarted");
            migrationBuilder.AddColumn<string>(name: "ImageUrl", table: "Users", type: "character varying(2048)", maxLength: 2048, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Nickname", table: "Users", type: "character varying(50)", maxLength: 50, nullable: true);
            migrationBuilder.AddColumn<string>(name: "PersonaInquiryId", table: "Users", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "PersonaInquiryStatus", table: "Users", type: "character varying(50)", maxLength: 50, nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "PersonaInquiryUpdatedAtUtc", table: "Users", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "PersonaVerifiedAtUtc", table: "Users", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PhoneNumber", table: "Users", type: "character varying(32)", maxLength: 32, nullable: true);
            migrationBuilder.AddColumn<string>(name: "RevenueCatAppUserId", table: "Users", type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "SubscriptionCurrentPeriodEndUtc", table: "Users", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<string>(name: "SubscriptionEntitlement", table: "Users", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "SubscriptionProductId", table: "Users", type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<string>(name: "SubscriptionStatus", table: "Users", type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "None");
            migrationBuilder.AddColumn<string>(name: "SubscriptionStore", table: "Users", type: "character varying(50)", maxLength: 50, nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "SubscriptionUpdatedAtUtc", table: "Users", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "SubscriptionWillRenew", table: "Users", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<string>(name: "VerifiedBirthdate", table: "Users", type: "character varying(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<string>(name: "VerifiedFirstName", table: "Users", type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<string>(name: "VerifiedLastName", table: "Users", type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<string>(name: "VerifiedLicenseState", table: "Users", type: "character varying(10)", maxLength: 10, nullable: true);
            migrationBuilder.AddColumn<string>(name: "VerifiedMiddleName", table: "Users", type: "character varying(200)", maxLength: 200, nullable: true);

            // Backfill back from sub-aggregates into Users before we drop those tables.
            migrationBuilder.Sql(@"
                UPDATE ""Users"" u SET
                  ""DisplayName"" = p.""DisplayName"",
                  ""Nickname"" = p.""Nickname"",
                  ""Handle"" = p.""Handle"",
                  ""ImageUrl"" = p.""ImageUrl"",
                  ""PhoneNumber"" = p.""PhoneNumber""
                FROM ""UserProfiles"" p WHERE p.""UserId"" = u.""Id"";
            ");

            migrationBuilder.Sql(@"
                UPDATE ""Users"" u SET
                  ""IdentityVerificationStatus"" = iv.""Status"",
                  ""PersonaInquiryId"" = iv.""PersonaInquiryId"",
                  ""PersonaInquiryStatus"" = iv.""PersonaInquiryStatus"",
                  ""PersonaInquiryUpdatedAtUtc"" = iv.""PersonaInquiryUpdatedAtUtc"",
                  ""VerifiedFirstName"" = iv.""VerifiedFirstName"",
                  ""VerifiedMiddleName"" = iv.""VerifiedMiddleName"",
                  ""VerifiedLastName"" = iv.""VerifiedLastName"",
                  ""VerifiedBirthdate"" = iv.""VerifiedBirthdate"",
                  ""VerifiedLicenseState"" = iv.""VerifiedLicenseState"",
                  ""PersonaVerifiedAtUtc"" = iv.""PersonaVerifiedAtUtc""
                FROM ""UserIdentityVerifications"" iv WHERE iv.""UserId"" = u.""Id"";
            ");

            migrationBuilder.Sql(@"
                UPDATE ""Users"" u SET
                  ""BackgroundCheckBadge"" = bc.""Badge"",
                  ""BackgroundCheckBadgeExpiresAtUtc"" = bc.""BadgeExpiresAtUtc"",
                  ""CheckrProfileId"" = bc.""CheckrProfileId"",
                  ""CheckrLastCheckId"" = bc.""CheckrLastCheckId"",
                  ""CheckrLastCheckAtUtc"" = bc.""CheckrLastCheckAtUtc"",
                  ""CheckrLastCheckHasPossibleMatches"" = bc.""CheckrLastCheckHasPossibleMatches""
                FROM ""UserBackgroundChecks"" bc WHERE bc.""UserId"" = u.""Id"";
            ");

            migrationBuilder.Sql(@"
                UPDATE ""Users"" u SET
                  ""SubscriptionStatus"" = sub.""Status"",
                  ""SubscriptionEntitlement"" = sub.""Entitlement"",
                  ""SubscriptionProductId"" = sub.""ProductId"",
                  ""SubscriptionStore"" = sub.""Store"",
                  ""SubscriptionCurrentPeriodEndUtc"" = sub.""CurrentPeriodEndUtc"",
                  ""SubscriptionWillRenew"" = sub.""WillRenew"",
                  ""SubscriptionUpdatedAtUtc"" = sub.""UpdatedAtUtc"",
                  ""RevenueCatAppUserId"" = sub.""RevenueCatAppUserId""
                FROM ""UserSubscriptions"" sub WHERE sub.""UserId"" = u.""Id"";
            ");

            // Drop the sub-aggregate tables.
            migrationBuilder.DropTable(name: "UserBackgroundChecks");
            migrationBuilder.DropTable(name: "UserIdentityVerifications");
            migrationBuilder.DropTable(name: "UserProfiles");
            migrationBuilder.DropTable(name: "UserSubscriptions");

            // Restore the unique indexes on Users.
            migrationBuilder.CreateIndex(name: "IX_Users_CheckrProfileId", table: "Users", column: "CheckrProfileId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Users_Handle", table: "Users", column: "Handle", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Users_PersonaInquiryId", table: "Users", column: "PersonaInquiryId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Users_RevenueCatAppUserId", table: "Users", column: "RevenueCatAppUserId", unique: true);
        }
    }
}
