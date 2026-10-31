using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ultron.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KodlamaIsleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodingJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Folder = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Request = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    LastMessage = table.Column<string>(type: "TEXT", nullable: true),
                    Progress = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CostUsd = table.Column<double>(type: "REAL", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodingJobs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodingJobs");
        }
    }
}
