using Errandy.Domain.Entities;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Errandy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationStatusesToRunner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT FALSE;

                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "BankAccountName" text NULL;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "BankAccountNumber" text NULL;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "BankName" text NULL;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "BankVerificationStatus" integer NOT NULL DEFAULT 0;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "Bvn" text NULL;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "GovernmentIdImageUrl" text NULL;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "GovernmentIdNumber" text NULL;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "GovernmentIdType" text NULL;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "GovernmentIdVerificationStatus" integer NOT NULL DEFAULT 0;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "Level" integer NOT NULL DEFAULT 0;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "PhoneVerificationStatus" integer NOT NULL DEFAULT 0;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "SelfieImageUrl" text NULL;
                ALTER TABLE "RunnerProfiles" ADD COLUMN IF NOT EXISTS "SelfieVerificationStatus" integer NOT NULL DEFAULT 0;

                CREATE TABLE IF NOT EXISTS "WithdrawalRequests" (
                    "Id" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "WalletId" uuid NOT NULL,
                    "TransactionId" uuid NOT NULL,
                    "Amount" numeric(18,2) NOT NULL,
                    "Currency" character varying(10) NOT NULL DEFAULT 'NGN',
                    "BankName" character varying(120) NOT NULL,
                    "BankCode" character varying(20) NULL,
                    "AccountNumber" character varying(20) NOT NULL,
                    "AccountName" character varying(150) NOT NULL,
                    "Status" integer NOT NULL,
                    "Reference" character varying(100) NOT NULL,
                    "RequestedAt" timestamp with time zone NOT NULL,
                    "EligibleAt" timestamp with time zone NOT NULL,
                    "ProcessedAt" timestamp with time zone NULL,
                    "ProcessedByAdminId" uuid NULL,
                    "AdminNote" character varying(500) NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_WithdrawalRequests" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_WithdrawalRequests_Reference"
                    ON "WithdrawalRequests" ("Reference");

                CREATE INDEX IF NOT EXISTS "IX_WithdrawalRequests_Status_EligibleAt"
                    ON "WithdrawalRequests" ("Status", "EligibleAt");

                CREATE INDEX IF NOT EXISTS "IX_WithdrawalRequests_UserId_RequestedAt"
                    ON "WithdrawalRequests" ("UserId", "RequestedAt");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BankAccountName",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "BankVerificationStatus",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "Bvn",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "GovernmentIdImageUrl",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "GovernmentIdNumber",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "GovernmentIdType",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "GovernmentIdVerificationStatus",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "PhoneVerificationStatus",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "SelfieImageUrl",
                table: "RunnerProfiles");

            migrationBuilder.DropColumn(
                name: "SelfieVerificationStatus",
                table: "RunnerProfiles");
        }
    }
}
