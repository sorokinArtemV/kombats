using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Combats.Services.Battle.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInobox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => new { x.message_id, x.consumer_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_expires_at",
                table: "inbox_messages",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_status",
                table: "inbox_messages",
                column: "status");
        }
    }
}
