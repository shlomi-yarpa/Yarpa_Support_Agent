using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yarpa.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteCustomerCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SiteCustomerCode",
                table: "YarpaAgent_Machines",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SiteCustomerCode",
                table: "YarpaAgent_Machines");
        }
    }
}
