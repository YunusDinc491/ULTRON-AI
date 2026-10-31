using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ultron.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeceDenetimi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnswerReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AskedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Question = table.Column<string>(type: "TEXT", nullable: false),
                    Answer = table.Column<string>(type: "TEXT", nullable: false),
                    Sources = table.Column<string>(type: "TEXT", nullable: false),
                    Embedding = table.Column<byte[]>(type: "BLOB", nullable: false),
                    AuditedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Verdict = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Correction = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    Reference = table.Column<string>(type: "TEXT", nullable: true),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerReviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnswerReviews_AuditedAtUtc",
                table: "AnswerReviews",
                column: "AuditedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerReviews_Decision",
                table: "AnswerReviews",
                column: "Decision");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnswerReviews");
        }
    }
}
