
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Interfaces;

public interface IPaymentTrackingService
{
    Task<PreparedPaymentDto> PrepareAsync(
        string ownerAccountId,
        string idempotencyKey,
        AuthorizePaymentRequest request,
        CancellationToken cancellationToken = default, Guid? assessmentId = null, Guid? approvalId = null);

    Task<PaymentTrackingDto?> GetAsync(
        string paymentId,
        CancellationToken cancellationToken = default);

    Task<PaymentTrackingDto?> SyncAsync(
        string paymentId,
        CancellationToken cancellationToken = default);


    Task<TransactionVerificationDto> ReportTransactionAsync(
        string paymentId,
        string accountId,
        ReportTransactionRequest request,
        CancellationToken cancellationToken = default);

}

public sealed record PaymentTrackingDto(
    string PaymentId,
    string Recipient,
    string AmountYocto,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
);
