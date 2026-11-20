using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Server.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "HistoryRecords",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "HistoryRecords");
        }
    }
}
