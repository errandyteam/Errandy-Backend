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
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT TRUE;

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

               """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

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
