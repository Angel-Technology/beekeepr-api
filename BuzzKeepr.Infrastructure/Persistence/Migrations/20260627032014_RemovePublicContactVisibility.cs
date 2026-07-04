using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemovePublicContactVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The ContactVisibility enum dropped the `Public` value — see
            // BuzzKeepr.Domain/Enums/ContactVisibility.cs. The column is stored as a string via
            // HasConversion<string>(), so EF doesn't see a schema change, but any rows still
            // holding 'Public' would fail to deserialize on the next read. Migrate them to
            // 'ConnectionsOnly' (preserves the user's "I opted to share" intent with the closest
            // remaining semantic — friends still see, strangers no longer do).
            //
            // Idempotent — re-running is a no-op once all rows are flipped.
            migrationBuilder.Sql(@"
                UPDATE ""UserProfiles""
                SET ""ContactVisibility"" = 'ConnectionsOnly'
                WHERE ""ContactVisibility"" = 'Public';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse migration is a no-op — we can't tell which ConnectionsOnly rows were
            // originally Public vs. originally ConnectionsOnly. If you need to roll back the
            // enum change in code, re-add 'Public' to the C# enum and the data still works
            // (the rows just stay at ConnectionsOnly).
        }
    }
}
