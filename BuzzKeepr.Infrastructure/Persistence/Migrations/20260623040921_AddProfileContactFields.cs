using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuzzKeepr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileContactFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactVisibility",
                table: "UserProfiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Private");

            migrationBuilder.AddColumn<string>(
                name: "GoogleVoicePhone",
                table: "UserProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramHandle",
                table: "UserProfiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileVisibility",
                table: "UserProfiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Public");

            migrationBuilder.AddColumn<string>(
                name: "SignalPhone",
                table: "UserProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramHandle",
                table: "UserProfiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhone",
                table: "UserProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactVisibility",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "GoogleVoicePhone",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "InstagramHandle",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ProfileVisibility",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SignalPhone",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "TelegramHandle",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "WhatsAppPhone",
                table: "UserProfiles");
        }
    }
}
