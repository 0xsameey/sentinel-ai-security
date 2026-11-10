
namespace Sentinel.Application.DTOs;

public sealed record ReportTransactionRequest(
    string TransactionHash,
    string Operation
);

public sealed record TransactionVerificationDto(
    string TransactionHash,
    string PaymentId,
    string Operation,
    string Status
);
