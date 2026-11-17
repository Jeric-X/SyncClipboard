using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Core.Migrations
{
    /// <inheritdoc />
    public partial class PreserveUnknownHistoryTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Released beta1 databases still have NOT NULL DEFAULT '[]'. Preserve their stored values.
            migrationBuilder.Sql("""
                ALTER TABLE HistoryRecords RENAME COLUMN Tags TO BetaTags;
                ALTER TABLE HistoryRecords ADD COLUMN Tags TEXT;
                UPDATE HistoryRecords SET Tags = BetaTags WHERE BetaTags IS NOT NULL;
                ALTER TABLE HistoryRecords DROP COLUMN BetaTags;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Copy verbatim: beta1's NOT NULL constraint aborts the rollback if any tags are unknown.
            migrationBuilder.Sql("""
                ALTER TABLE HistoryRecords RENAME COLUMN Tags TO NullableTags;
                ALTER TABLE HistoryRecords ADD COLUMN Tags TEXT NOT NULL DEFAULT '[]';
                UPDATE HistoryRecords SET Tags = NullableTags;
                ALTER TABLE HistoryRecords DROP COLUMN NullableTags;
                """);
        }
    }
}
