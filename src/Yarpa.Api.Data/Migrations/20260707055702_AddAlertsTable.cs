using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yarpa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    AlertId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AlertType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourceSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceChangeId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.AlertId);
                    table.ForeignKey(
                        name: "FK_Alerts_Changes_SourceChangeId",
                        column: x => x.SourceChangeId,
                        principalTable: "Changes",
                        principalColumn: "ChangeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Alerts_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "MachineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Alerts_Snapshots_SourceSnapshotId",
                        column: x => x.SourceSnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "SnapshotId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_MachineId_AlertType_State",
                table: "Alerts",
                columns: new[] { "MachineId", "AlertType", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_SourceChangeId",
                table: "Alerts",
                column: "SourceChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_SourceSnapshotId",
                table: "Alerts",
                column: "SourceSnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");
        }
    }
}
