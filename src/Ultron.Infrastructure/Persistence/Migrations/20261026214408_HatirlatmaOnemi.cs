using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ultron.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HatirlatmaOnemi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NagAtUtc",
                table: "Reminders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "Reminders",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Onemsiz"); // eski hatırlatıcılar: önemsiz (bir kez)

            migrationBuilder.AddColumn<string>(
                name: "SpokenText",
                table: "Reminders",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReminderPreferences",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderPreferences", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReminderPreferences");

            migrationBuilder.DropColumn(
                name: "NagAtUtc",
                table: "Reminders");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Reminders");

            migrationBuilder.DropColumn(
                name: "SpokenText",
                table: "Reminders");
        }
    }
}
