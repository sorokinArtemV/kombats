using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kombats.Matchmaking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchmakingOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matchmaking_outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matchmaking_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_PlayerAId_CreatedAtUtc",
                table: "matches",
                columns: new[] { "PlayerAId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_matches_PlayerBId_CreatedAtUtc",
                table: "matches",
                columns: new[] { "PlayerBId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_OccurredAtUtc",
                table: "matchmaking_outbox_messages",
                columns: new[] { "Status", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matchmaking_outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_matches_PlayerAId_CreatedAtUtc",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_PlayerBId_CreatedAtUtc",
                table: "matches");
        }
    }
}
