using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ultron.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Kutuphane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryPages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsStale = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryPages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LibraryChunks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PageId = table.Column<long>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    Embedding = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryChunks_LibraryPages_PageId",
                        column: x => x.PageId,
                        principalTable: "LibraryPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryChunks_PageId",
                table: "LibraryChunks",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryPages_SourceId_Path",
                table: "LibraryPages",
                columns: new[] { "SourceId", "Path" },
                unique: true);

            // Kelime araması (FTS5): rowid = LibraryChunks.Id, metin normalleştirilmiş (EfLibraryStore yazar).
            migrationBuilder.Sql("CREATE VIRTUAL TABLE LibrarySearch USING fts5(Text, tokenize = 'unicode61');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE LibrarySearch;");

            migrationBuilder.DropTable(
                name: "LibraryChunks");

            migrationBuilder.DropTable(
                name: "LibraryPages");
        }
    }
}
