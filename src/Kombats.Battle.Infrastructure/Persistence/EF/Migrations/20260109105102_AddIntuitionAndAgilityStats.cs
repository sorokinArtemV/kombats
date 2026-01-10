using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kombats.Battle.Infrastructure.Persistence.EF.Migrations
{
    /// <inheritdoc />
    public partial class AddIntuitionAndAgilityStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Agility",
                table: "player_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Intuition",
                table: "player_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Agility",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "Intuition",
                table: "player_profiles");
        }
    }
}
