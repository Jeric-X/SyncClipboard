using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Server.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "From",
                table: "HistoryRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "From",
                table: "HistoryRecords");
        }
    }
}
