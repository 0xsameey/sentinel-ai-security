using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DelegatedSpending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delegated_payments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DelegationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContractId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    IntentJson = table.Column<string>(type: "jsonb", nullable: false),
                    IntentHash = table.Column<string>(type: "text", nullable: false),
                    DecisionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Decision = table.Column<string>(type: "text", nullable: false),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovalExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovalFingerprint = table.Column<string>(type: "text", nullable: true),
                    PermitId = table.Column<Guid>(type: "uuid", nullable: true),
                    PermitFingerprint = table.Column<string>(type: "text", nullable: true),
                    SignerPublicKey = table.Column<string>(type: "text", nullable: true),
                    PermitExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TransactionHash = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delegated_payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delegations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContractId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    StateJson = table.Column<string>(type: "jsonb", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delegations", x => new { x.ContractId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_delegated_payments_ContractId_DelegationId",
                table: "delegated_payments",
                columns: new[] { "ContractId", "DelegationId" });

            migrationBuilder.CreateIndex(
                name: "IX_delegated_payments_PermitId",
                table: "delegated_payments",
                column: "PermitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delegated_payments_TransactionHash",
                table: "delegated_payments",
                column: "TransactionHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delegated_payments");

            migrationBuilder.DropTable(
                name: "delegations");
        }
    }
}
