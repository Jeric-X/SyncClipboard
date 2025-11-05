using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Core.Migrations
{
    /// <inheritdoc />
    public partial class Add_SyncStatus_LastModified_Version_IsDeleted_IsLocalFileReady_Size_From : Migration
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

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "HistoryRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsLocalFileReady",
                table: "HistoryRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModified",
                table: "HistoryRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "Size",
                table: "HistoryRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "SyncStatus",
                table: "HistoryRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "HistoryRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "From",
                table: "HistoryRecords");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "HistoryRecords");

            migrationBuilder.DropColumn(
                name: "IsLocalFileReady",
                table: "HistoryRecords");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "HistoryRecords");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "HistoryRecords");

            migrationBuilder.DropColumn(
                name: "SyncStatus",
                table: "HistoryRecords");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "HistoryRecords");
        }
    }
}
