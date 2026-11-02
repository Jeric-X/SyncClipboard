using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Core.Migrations;

public partial class AddHistoryTags : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Tags", table: "HistoryRecords", type: "TEXT", nullable: false, defaultValue: "[]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Tags", table: "HistoryRecords");
    }
}
