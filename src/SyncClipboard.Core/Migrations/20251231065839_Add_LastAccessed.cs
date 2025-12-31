using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Core.Migrations
{
    /// <inheritdoc />
    public partial class Add_LastAccessed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastAccessed",
                table: "HistoryRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAccessed",
                table: "HistoryRecords");
        }
    }
}
