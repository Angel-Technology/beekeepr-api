using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pg_trgm powers similarity()/ILIKE acceleration via GIN indexes below.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // Handles are stored lower-cased, but prefix-ILIKE still wants a lower() expression index
            // so HotChocolate's case-insensitive lookups stay sargable. Partial on non-deleted rows only.
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_Users_Handle_Lower""
                  ON ""Users"" (lower(""Handle"") varchar_pattern_ops)
                  WHERE ""DeletedAtUtc"" IS NULL AND ""Handle"" IS NOT NULL;");

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_Users_Nickname_Trgm""
                  ON ""Users"" USING gin (""Nickname"" gin_trgm_ops)
                  WHERE ""DeletedAtUtc"" IS NULL AND ""Nickname"" IS NOT NULL;");

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_Users_DisplayName_Trgm""
                  ON ""Users"" USING gin (""DisplayName"" gin_trgm_ops)
                  WHERE ""DeletedAtUtc"" IS NULL AND ""DisplayName"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Users_DisplayName_Trgm"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Users_Nickname_Trgm"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Users_Handle_Lower"";");
            // Leave pg_trgm installed; other migrations or queries may rely on it.
        }
    }
}
