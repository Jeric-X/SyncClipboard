using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryTransferData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TransferDataFile",
                table: "HistoryRecords",
                type: "TEXT",
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "TransferDataHash",
                table: "HistoryRecords",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TransferDataFile",
                table: "HistoryRecords");

            migrationBuilder.DropColumn(
                name: "TransferDataHash",
                table: "HistoryRecords");
        }
    }
}
