using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gamex.GameCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGameCoverSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cover_source_url",
                table: "games",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cover_source_url",
                table: "games");
        }
    }
}
