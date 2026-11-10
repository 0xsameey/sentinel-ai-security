using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecurityGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SecurityApprovalId",
                table: "payment_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityAssessmentId",
                table: "payment_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityIntentHash",
                table: "payment_requests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "counterparty_blocklist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceUri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    EvidenceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counterparty_blocklist", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "security_assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContractId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IntentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntentJson = table.Column<string>(type: "jsonb", nullable: false),
                    DecisionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Decision = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_assessments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "human_approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ConsumedByPaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_human_approvals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_human_approvals_security_assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "security_assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "execution_permits",
                columns: table => new
                {
                    PaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PermitId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uuid", nullable: true),
                    IntentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SignerPublicKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_permits", x => x.PaymentId);
                    table.ForeignKey(
                        name: "FK_execution_permits_human_approvals_ApprovalId",
                        column: x => x.ApprovalId,
                        principalTable: "human_approvals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_execution_permits_security_assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "security_assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_requests_SecurityApprovalId",
                table: "payment_requests",
                column: "SecurityApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_requests_SecurityAssessmentId",
                table: "payment_requests",
                column: "SecurityAssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_counterparty_blocklist_AccountId",
                table: "counterparty_blocklist",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_execution_permits_ApprovalId",
                table: "execution_permits",
                column: "ApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_execution_permits_AssessmentId",
                table: "execution_permits",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_execution_permits_PermitId",
                table: "execution_permits",
                column: "PermitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_human_approvals_AssessmentId",
                table: "human_approvals",
                column: "AssessmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_human_approvals_PaymentId_ExpiresAtUtc",
                table: "human_approvals",
                columns: new[] { "PaymentId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_security_assessments_ExpiresAtUtc",
                table: "security_assessments",
                column: "ExpiresAtUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_requests_human_approvals_SecurityApprovalId",
                table: "payment_requests",
                column: "SecurityApprovalId",
                principalTable: "human_approvals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_requests_security_assessments_SecurityAssessmentId",
                table: "payment_requests",
                column: "SecurityAssessmentId",
                principalTable: "security_assessments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_requests_human_approvals_SecurityApprovalId",
                table: "payment_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_requests_security_assessments_SecurityAssessmentId",
                table: "payment_requests");

            migrationBuilder.DropTable(
                name: "counterparty_blocklist");

            migrationBuilder.DropTable(
                name: "execution_permits");

            migrationBuilder.DropTable(
                name: "human_approvals");

            migrationBuilder.DropTable(
                name: "security_assessments");

            migrationBuilder.DropIndex(
                name: "IX_payment_requests_SecurityApprovalId",
                table: "payment_requests");

            migrationBuilder.DropIndex(
                name: "IX_payment_requests_SecurityAssessmentId",
                table: "payment_requests");

            migrationBuilder.DropColumn(
                name: "SecurityApprovalId",
                table: "payment_requests");

            migrationBuilder.DropColumn(
                name: "SecurityAssessmentId",
                table: "payment_requests");

            migrationBuilder.DropColumn(
                name: "SecurityIntentHash",
                table: "payment_requests");
        }
    }
}
