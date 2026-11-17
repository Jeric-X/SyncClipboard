using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Core.Migrations;

public partial class TrackHistoryBeforeTags : Migration
{
    internal static void CreateTracker(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS HistoryBeforeTags (ID INTEGER PRIMARY KEY);
            CREATE TRIGGER IF NOT EXISTS ForgetEditedHistoryTags AFTER UPDATE OF Tags ON HistoryRecords
            BEGIN
                DELETE FROM HistoryBeforeTags WHERE ID = NEW.ID;
            END;
            """);
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // This migration runs immediately before AddHistoryTags on a direct 3.3.3 upgrade.
        // A released beta1 database has already applied AddHistoryTags, so none of its rows are marked.
        CreateTracker(migrationBuilder);
        migrationBuilder.Sql("""
            INSERT INTO HistoryBeforeTags (ID)
                SELECT ID FROM HistoryRecords WHERE NOT EXISTS (
                    SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20261027010000_AddHistoryTags'
                );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS ForgetEditedHistoryTags; DROP TABLE IF EXISTS HistoryBeforeTags;");
    }
}
