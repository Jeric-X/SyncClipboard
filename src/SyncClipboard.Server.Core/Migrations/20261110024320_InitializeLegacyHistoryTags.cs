using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Server.Core.Migrations
{
    /// <inheritdoc />
    public partial class InitializeLegacyHistoryTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Released servers never received tags; their empty arrays represent unknown tag state.
            migrationBuilder.Sql("UPDATE HistoryRecords SET Tags = NULL WHERE Tags = '[]';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE HistoryRecords SET Tags = '[]' WHERE Tags IS NULL;");
        }
    }
}
