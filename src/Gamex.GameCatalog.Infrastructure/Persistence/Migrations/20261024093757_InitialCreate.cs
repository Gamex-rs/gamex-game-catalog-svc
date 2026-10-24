using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gamex.GameCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    rawg_id = table.Column<int>(type: "int", nullable: true),
                    rawg_slug = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    title = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    cover_image_url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    release_date = table.Column<DateOnly>(type: "date", nullable: true),
                    metacritic = table.Column<short>(type: "smallint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_games", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "genres",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_genres", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "platforms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platforms", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "game_stats",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owned_count = table.Column<uint>(type: "int unsigned", nullable: false),
                    wishlist_count = table.Column<uint>(type: "int unsigned", nullable: false),
                    for_exchange_count = table.Column<uint>(type: "int unsigned", nullable: false),
                    for_sale_count = table.Column<uint>(type: "int unsigned", nullable: false),
                    want_to_buy_count = table.Column<uint>(type: "int unsigned", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_stats", x => x.game_id);
                    table.ForeignKey(
                        name: "fk_game_stats_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "game_genres",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    genre_id = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_genres", x => new { x.game_id, x.genre_id });
                    table.ForeignKey(
                        name: "fk_game_genres_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_game_genres_genres_genre_id",
                        column: x => x.genre_id,
                        principalTable: "genres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "game_platforms",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    platform_id = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_platforms", x => new { x.game_id, x.platform_id });
                    table.ForeignKey(
                        name: "fk_game_platforms_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_game_platforms_platforms_platform_id",
                        column: x => x.platform_id,
                        principalTable: "platforms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_game_genres_genre_id",
                table: "game_genres",
                column: "genre_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_platforms_platform_id",
                table: "game_platforms",
                column: "platform_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_stats_owned_count",
                table: "game_stats",
                column: "owned_count");

            migrationBuilder.CreateIndex(
                name: "ix_games_metacritic",
                table: "games",
                column: "metacritic");

            migrationBuilder.CreateIndex(
                name: "ix_games_release_date",
                table: "games",
                column: "release_date");

            migrationBuilder.CreateIndex(
                name: "ix_games_title",
                table: "games",
                column: "title");

            migrationBuilder.CreateIndex(
                name: "ux_games_rawg_id",
                table: "games",
                column: "rawg_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_games_rawg_slug",
                table: "games",
                column: "rawg_slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_genres_slug",
                table: "genres",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_platforms_slug",
                table: "platforms",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_genres");

            migrationBuilder.DropTable(
                name: "game_platforms");

            migrationBuilder.DropTable(
                name: "game_stats");

            migrationBuilder.DropTable(
                name: "genres");

            migrationBuilder.DropTable(
                name: "platforms");

            migrationBuilder.DropTable(
                name: "games");
        }
    }
}
