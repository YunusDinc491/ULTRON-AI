using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ultron.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Anahtar kelime araması için SQLite FTS5 sanal tablosu. EF modelinde yok (EF sanal tabloları tanımaz);
    /// EfMemoryStore elle yönetir. Mevcut kayıtlar açılışta RebuildSearchIndexIfNeededAsync ile doldurulur.
    /// </summary>
    public partial class HafizaArama : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // unicode61: harf/rakam dışı her şeyi ayırıcı sayar. Metin zaten TurkishText.Normalize'dan geçmiş olarak yazılır.
            migrationBuilder.Sql("CREATE VIRTUAL TABLE MemorySearch USING fts5(Text, tokenize = 'unicode61');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE MemorySearch;");
        }
    }
}
