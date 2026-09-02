using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Server.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryTransferDataHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "TransferDataHash",
                table: "HistoryRecords");
        }
    }
}
