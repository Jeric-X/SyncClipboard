using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Core.Migrations;

public partial class IndexHistoryIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Hash", table: "HistoryRecords", type: "TEXT", nullable: false,
            collation: "NOCASE", oldClrType: typeof(string), oldType: "TEXT");
        migrationBuilder.CreateIndex(
            name: "IX_HistoryRecords_Type_Hash", table: "HistoryRecords", columns: ["Type", "Hash"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_HistoryRecords_Type_Hash", table: "HistoryRecords");
        migrationBuilder.AlterColumn<string>(
            name: "Hash", table: "HistoryRecords", type: "TEXT", nullable: false,
            oldClrType: typeof(string), oldType: "TEXT", oldCollation: "NOCASE");
    }
}
