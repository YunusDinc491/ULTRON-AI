using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ultron.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Kategoriler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    IsCore = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "CategorySuggestions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Example = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorySuggestions", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Key", "CreatedAtUtc", "Description", "IsBuiltIn", "IsCore", "Name", "SortOrder" },
                values: new object[,]
                {
                    { "Finans", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Bütçe, gelir-gider, birikim, abonelikler, borçlar", true, false, "Finans", 8 },
                    { "GenelBilgi", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Kişisel olmayan genel bilgi", true, false, "Genel bilgi", 13 },
                    { "Hedefler", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Kısa ve uzun vadeli hedefler, planlar, yapmak istedikleri", true, false, "Hedefler", 10 },
                    { "Hobiler", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "İlgi alanları, oyunlar, spor, tuttuğu takımlar", true, false, "Hobiler & Spor", 9 },
                    { "Iliski", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Sevgilisi/partneri ve ilişkisiyle ilgili her şey (onun zevkleri, özel günler, hediyeler)", true, false, "İlişki", 5 },
                    { "Kariyer", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Staj, iş, başvurular, mesleki beceriler ve kariyer hedefleri", true, false, "Kariyer", 2 },
                    { "Kisiler", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Aile, arkadaşlar ve onlar hakkındaki bilgiler (başka birinin okulu/işi de buraya; sevgili hariç)", true, false, "Kişiler", 4 },
                    { "Okul", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Kullanıcının KENDİ okulu, dersleri, sınavları, notları", true, false, "Okul", 1 },
                    { "OzelHayat", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Evcil hayvan, ev, yaşadığı yer, özel konular", true, false, "Özel hayat", 6 },
                    { "Projeler", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Kullanıcının yürüttüğü projeler", true, false, "Projeler", 3 },
                    { "Rutinler", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Düzenli yaptığı şeyler, haftalık program", true, false, "Rutinler", 12 },
                    { "Saglik", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Sağlık durumu, alerji, ilaç, hastalık", true, true, "Sağlık", 7 },
                    { "Tercihler", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Sevdiği/sevmediği şeyler, zevkler", true, false, "Tercihler", 11 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "CategorySuggestions");
        }
    }
}
