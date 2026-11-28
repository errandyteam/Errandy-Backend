using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Errandy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReconciliationAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Wallets",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "PaymentReconciliationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResultCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReconciliationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationAttempts_PaymentIntents_PaymentIntentId",
                        column: x => x.PaymentIntentId,
                        principalTable: "PaymentIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationAttempts_Users_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationAttempts_AdminUserId_RequestedAt",
                table: "PaymentReconciliationAttempts",
                columns: new[] { "AdminUserId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationAttempts_Outcome_RequestedAt",
                table: "PaymentReconciliationAttempts",
                columns: new[] { "Outcome", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationAttempts_PaymentIntentId_RequestedAt",
                table: "PaymentReconciliationAttempts",
                columns: new[] { "PaymentIntentId", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentReconciliationAttempts");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Wallets");
        }
    }
}
