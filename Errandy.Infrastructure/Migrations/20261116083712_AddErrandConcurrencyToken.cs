using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Errandy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErrandConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Errands",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "WithdrawalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "NGN"),
                    BankName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BankCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AccountNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EligibleAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdminNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithdrawalRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_Reference",
                table: "WithdrawalRequests",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_Status_EligibleAt",
                table: "WithdrawalRequests",
                columns: new[] { "Status", "EligibleAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_UserId_RequestedAt",
                table: "WithdrawalRequests",
                columns: new[] { "UserId", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Errands");
        }
    }
}
