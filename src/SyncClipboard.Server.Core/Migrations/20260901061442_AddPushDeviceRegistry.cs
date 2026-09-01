using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncClipboard.Server.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPushDeviceRegistry : Migration
    {
        private static readonly string[] ProviderLastUpdatedColumns = ["Provider", "LastUpdated"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PushDeviceRegistrations",
                columns: table => new
                {
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PushToken = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    AppVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LastUpdated = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDeviceRegistrations", x => x.DeviceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushDeviceRegistrations_Provider_LastUpdated",
                table: "PushDeviceRegistrations",
                columns: ProviderLastUpdatedColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushDeviceRegistrations");
        }
    }
}
