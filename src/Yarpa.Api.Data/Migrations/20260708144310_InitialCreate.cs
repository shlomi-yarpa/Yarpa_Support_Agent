using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yarpa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YarpaAgent_Customers",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YarpaAgent_Customers", x => x.CustomerId);
                });

            migrationBuilder.CreateTable(
                name: "YarpaAgent_ApiKeys",
                columns: table => new
                {
                    ApiKeyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YarpaAgent_ApiKeys", x => x.ApiKeyId);
                    table.ForeignKey(
                        name: "FK_YarpaAgent_ApiKeys_YarpaAgent_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "YarpaAgent_Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "YarpaAgent_Machines",
                columns: table => new
                {
                    MachineId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComputerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YarpaAgent_Machines", x => x.MachineId);
                    table.ForeignKey(
                        name: "FK_YarpaAgent_Machines_YarpaAgent_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "YarpaAgent_Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "YarpaAgent_Snapshots",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CollectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AgentVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OsCaption = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OsBuild = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RamTotalMb = table.Column<long>(type: "bigint", nullable: true),
                    MinFreeDiskPercent = table.Column<double>(type: "float", nullable: true),
                    YarpaVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SqlInstalled = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YarpaAgent_Snapshots", x => x.SnapshotId);
                    table.ForeignKey(
                        name: "FK_YarpaAgent_Snapshots_YarpaAgent_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "YarpaAgent_Machines",
                        principalColumn: "MachineId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "YarpaAgent_Changes",
                columns: table => new
                {
                    ChangeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SectionName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DetectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YarpaAgent_Changes", x => x.ChangeId);
                    table.ForeignKey(
                        name: "FK_YarpaAgent_Changes_YarpaAgent_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "YarpaAgent_Machines",
                        principalColumn: "MachineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YarpaAgent_Changes_YarpaAgent_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "YarpaAgent_Snapshots",
                        principalColumn: "SnapshotId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "YarpaAgent_Alerts",
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
                    table.PrimaryKey("PK_YarpaAgent_Alerts", x => x.AlertId);
                    table.ForeignKey(
                        name: "FK_YarpaAgent_Alerts_YarpaAgent_Changes_SourceChangeId",
                        column: x => x.SourceChangeId,
                        principalTable: "YarpaAgent_Changes",
                        principalColumn: "ChangeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YarpaAgent_Alerts_YarpaAgent_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "YarpaAgent_Machines",
                        principalColumn: "MachineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YarpaAgent_Alerts_YarpaAgent_Snapshots_SourceSnapshotId",
                        column: x => x.SourceSnapshotId,
                        principalTable: "YarpaAgent_Snapshots",
                        principalColumn: "SnapshotId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "YarpaAgent_Customers",
                columns: new[] { "CustomerId", "CreatedAtUtc", "Name" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Yarpa Dev" });

            migrationBuilder.InsertData(
                table: "YarpaAgent_ApiKeys",
                columns: new[] { "ApiKeyId", "CreatedAtUtc", "CustomerId", "IsActive", "KeyHash", "RevokedAtUtc" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("00000000-0000-0000-0000-000000000001"), true, "6ae2432f025fb1ccc63518e80b02ef5ff04e1b19a541cbfa4024ebeff47bdd19", null });

            migrationBuilder.CreateIndex(
                name: "IX_YarpaAgent_Alerts_MachineId_AlertType_State",
                table: "YarpaAgent_Alerts",
                columns: new[] { "MachineId", "AlertType", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_YarpaAgent_Alerts_SourceChangeId",
                table: "YarpaAgent_Alerts",
                column: "SourceChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_YarpaAgent_Alerts_SourceSnapshotId",
                table: "YarpaAgent_Alerts",
                column: "SourceSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_YarpaAgent_ApiKeys_CustomerId",
                table: "YarpaAgent_ApiKeys",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_YarpaAgent_ApiKeys_KeyHash",
                table: "YarpaAgent_ApiKeys",
                column: "KeyHash");

            migrationBuilder.CreateIndex(
                name: "IX_YarpaAgent_Changes_MachineId_DetectedAtUtc",
                table: "YarpaAgent_Changes",
                columns: new[] { "MachineId", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_YarpaAgent_Changes_SnapshotId",
                table: "YarpaAgent_Changes",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_YarpaAgent_Machines_CustomerId",
                table: "YarpaAgent_Machines",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_YarpaAgent_Snapshots_MachineId_CollectedAtUtc",
                table: "YarpaAgent_Snapshots",
                columns: new[] { "MachineId", "CollectedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YarpaAgent_Alerts");

            migrationBuilder.DropTable(
                name: "YarpaAgent_ApiKeys");

            migrationBuilder.DropTable(
                name: "YarpaAgent_Changes");

            migrationBuilder.DropTable(
                name: "YarpaAgent_Snapshots");

            migrationBuilder.DropTable(
                name: "YarpaAgent_Machines");

            migrationBuilder.DropTable(
                name: "YarpaAgent_Customers");
        }
    }
}
