using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ultron.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HafizaCekmeceleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // tasarım kararı (2026-10-28): hafıza sıfırlanır (eski 13 kategoriye göre dizilmişti); kurulumdan önce yedek alındı.
            // Klasörleri bundan sonra Ultron PATRON / İŞ / OKUL / BİLGİ çekmecelerinde kendisi açar.
            migrationBuilder.Sql("DELETE FROM Memories;");
            migrationBuilder.Sql("DELETE FROM MemorySearch;");
            migrationBuilder.Sql("DELETE FROM CategorySuggestions;");
            migrationBuilder.Sql("DELETE FROM Categories WHERE IsBuiltIn = 0;");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Finans");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "GenelBilgi");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Hedefler");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Hobiler");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Iliski");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Kariyer");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Kisiler");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Okul");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "OzelHayat");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Projeler");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Rutinler");

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Tercihler");

            migrationBuilder.AddColumn<string>(
                name: "Drawer",
                table: "Categories",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Patron");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Saglik",
                columns: new[] { "Drawer", "SortOrder" },
                values: new object[] { "Patron", 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Drawer",
                table: "Categories");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Key",
                keyValue: "Saglik",
                column: "SortOrder",
                value: 7);

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
                    { "Tercihler", new DateTime(2026, 10, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Sevdiği/sevmediği şeyler, zevkler", true, false, "Tercihler", 11 }
                });
        }
    }
}
