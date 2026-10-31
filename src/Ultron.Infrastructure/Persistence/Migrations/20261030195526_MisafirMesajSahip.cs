using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ultron.Infrastructure.Persistence.Migrations
{
    /// <summary>Eski bir veri düzeltmesiydi (misafir mesajı türünün adı); misafir tablosu sonraki geçişte kaldırıldığı için artık bir şey yapmaz.</summary>
    public partial class MisafirMesajSahip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
