using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRiskAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiExplanation",
                table: "payment_requests",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "payment_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RiskDecision",
                table: "payment_requests",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RiskLevel",
                table: "payment_requests",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RiskReasonsJson",
                table: "payment_requests",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "RiskScore",
                table: "payment_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiExplanation",
                table: "payment_requests");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "payment_requests");

            migrationBuilder.DropColumn(
                name: "RiskDecision",
                table: "payment_requests");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                table: "payment_requests");

            migrationBuilder.DropColumn(
                name: "RiskReasonsJson",
                table: "payment_requests");

            migrationBuilder.DropColumn(
                name: "RiskScore",
                table: "payment_requests");
        }
    }
}
