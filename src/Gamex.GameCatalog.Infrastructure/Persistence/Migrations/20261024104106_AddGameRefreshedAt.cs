using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gamex.GameCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGameRefreshedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "refreshed_at",
                table: "games",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_games_refreshed_at",
                table: "games",
                column: "refreshed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_games_refreshed_at",
                table: "games");

            migrationBuilder.DropColumn(
                name: "refreshed_at",
                table: "games");
        }
    }
}
