using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gamex.GameCatalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedLibraryEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_library_events",
                columns: table => new
                {
                    message_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_library_events", x => x.message_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_processed_library_events_processed_at",
                table: "processed_library_events",
                column: "processed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_library_events");
        }
    }
}
