using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Core.Migrations
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
            // Keep the downgrade transactional if an earlier migration cannot be reverted.
            migrationBuilder.Sql("ALTER TABLE HistoryRecords DROP COLUMN Title");
        }
    }
}
