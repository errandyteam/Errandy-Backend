using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Errandy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticPaymentChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomaticPaymentChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    LastResult = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AttemptsStarted = table.Column<int>(type: "integer", nullable: false),
                    MaximumAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomaticPaymentChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomaticPaymentChecks_PaymentIntents_PaymentIntentId",
                        column: x => x.PaymentIntentId,
                        principalTable: "PaymentIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomaticPaymentChecks_PaymentIntentId",
                table: "AutomaticPaymentChecks",
                column: "PaymentIntentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomaticPaymentChecks_Status_LeaseExpiresAt_Id",
                table: "AutomaticPaymentChecks",
                columns: new[] { "Status", "LeaseExpiresAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomaticPaymentChecks_Status_NextAttemptAt_Id",
                table: "AutomaticPaymentChecks",
                columns: new[] { "Status", "NextAttemptAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomaticPaymentChecks");
        }
    }
}
