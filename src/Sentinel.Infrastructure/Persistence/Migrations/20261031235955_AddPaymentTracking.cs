using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OwnerAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ContractId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AmountYocto = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                    ExpiresAtNs = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payment_transaction_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SignerAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TransactionHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_transaction_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_transaction_attempts_payment_requests_PaymentReques~",
                        column: x => x.PaymentRequestId,
                        principalTable: "payment_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_requests_ContractId_OwnerAccountId_IdempotencyKey",
                table: "payment_requests",
                columns: new[] { "ContractId", "OwnerAccountId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_requests_CreatedAtUtc",
                table: "payment_requests",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_payment_requests_PaymentId",
                table: "payment_requests",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_transaction_attempts_PaymentRequestId_Operation",
                table: "payment_transaction_attempts",
                columns: new[] { "PaymentRequestId", "Operation" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_transaction_attempts_TransactionHash",
                table: "payment_transaction_attempts",
                column: "TransactionHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_transaction_attempts");

            migrationBuilder.DropTable(
                name: "payment_requests");
        }
    }
}
