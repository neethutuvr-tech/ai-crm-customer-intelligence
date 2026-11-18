using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Leads_ConvertedCustomerId",
                table: "Leads",
                column: "ConvertedCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_OrganizationId_Email",
                table: "Leads",
                columns: new[] { "OrganizationId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leads_OwnerUserId",
                table: "Leads",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_AspNetUsers_OwnerUserId",
                table: "Leads",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_Customers_ConvertedCustomerId",
                table: "Leads",
                column: "ConvertedCustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leads_AspNetUsers_OwnerUserId",
                table: "Leads");

            migrationBuilder.DropForeignKey(
                name: "FK_Leads_Customers_ConvertedCustomerId",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_ConvertedCustomerId",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_OrganizationId_Email",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_OwnerUserId",
                table: "Leads");
        }
    }
}
