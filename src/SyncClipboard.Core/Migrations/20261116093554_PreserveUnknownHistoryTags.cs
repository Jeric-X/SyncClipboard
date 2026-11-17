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
            // Scripts starting at beta1 skip the earlier tracker migration. Such databases have no unknown-row markers.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS HistoryBeforeTags (ID INTEGER PRIMARY KEY);
                INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion)
                    VALUES ('20261027005959_TrackHistoryBeforeTags', '9.0.20');
                DROP TRIGGER IF EXISTS ForgetEditedHistoryTags;
                ALTER TABLE HistoryRecords RENAME COLUMN Tags TO BetaTags;
                ALTER TABLE HistoryRecords ADD COLUMN Tags TEXT;
                UPDATE HistoryRecords SET Tags = BetaTags
                WHERE BetaTags <> '[]' OR ID NOT IN (SELECT ID FROM HistoryBeforeTags);
                ALTER TABLE HistoryRecords DROP COLUMN BetaTags;
                DROP TABLE HistoryBeforeTags;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the released beta1 schema, including the default for inserts that omit Tags.
            migrationBuilder.Sql("""
                CREATE TABLE HistoryBeforeTags (ID INTEGER PRIMARY KEY);
                INSERT INTO HistoryBeforeTags (ID) SELECT ID FROM HistoryRecords WHERE Tags IS NULL;
                ALTER TABLE HistoryRecords RENAME COLUMN Tags TO NullableTags;
                ALTER TABLE HistoryRecords ADD COLUMN Tags TEXT NOT NULL DEFAULT '[]';
                UPDATE HistoryRecords SET Tags = NullableTags WHERE NullableTags IS NOT NULL;
                ALTER TABLE HistoryRecords DROP COLUMN NullableTags;
                """);
            TrackHistoryBeforeTags.CreateTracker(migrationBuilder);
        }
    }
}
