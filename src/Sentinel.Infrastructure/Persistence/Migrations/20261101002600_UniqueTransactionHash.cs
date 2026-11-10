using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueTransactionHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_transaction_attempts_TransactionHash",
                table: "payment_transaction_attempts");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transaction_attempts_TransactionHash",
                table: "payment_transaction_attempts",
                column: "TransactionHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_transaction_attempts_TransactionHash",
                table: "payment_transaction_attempts");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transaction_attempts_TransactionHash",
                table: "payment_transaction_attempts",
                column: "TransactionHash");
        }
    }
}
