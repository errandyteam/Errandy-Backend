using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Errandy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceAdjustmentAndTimePreferenceToErrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ProposedCost",
                table: "Errands",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProposedCostAt",
                table: "Errands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedCostReason",
                table: "Errands",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimePreference",
                table: "Errands",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposedCost",
                table: "Errands");

            migrationBuilder.DropColumn(
                name: "ProposedCostAt",
                table: "Errands");

            migrationBuilder.DropColumn(
                name: "ProposedCostReason",
                table: "Errands");

            migrationBuilder.DropColumn(
                name: "TimePreference",
                table: "Errands");
        }
    }
}
