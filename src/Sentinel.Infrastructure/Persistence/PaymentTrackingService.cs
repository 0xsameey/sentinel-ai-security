
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Sentinel.Application.DTOs;
using Sentinel.Application.Interfaces;
using Sentinel.Domain.Entities;
using System.Text.Json;
using Sentinel.Application.Exceptions;
using Sentinel.Infrastructure.Near;
using Sentinel.Application.Security;

namespace Sentinel.Infrastructure.Persistence;

public sealed class PaymentTrackingService
    : IPaymentTrackingService
{
    private readonly SentinelDbContext _db;
    private readonly IPaymentTransactionService _transactions;
    private readonly INearContractService _contract;
    private readonly NearOptions _near;
    private readonly NearTransactionVerifier _verifier;
    private readonly ISecurityGateway _gateway;

    public PaymentTrackingService(
        SentinelDbContext db,
        IPaymentTransactionService transactions,
        INearContractService contract,
        NearTransactionVerifier verifier,
        IOptions<NearOptions> near,
        ISecurityGateway gateway)
    {
        _db = db;
        _transactions = transactions;
        _contract = contract;
        _near = near.Value;
        _verifier = verifier;
        _gateway = gateway;
    }



    public async Task<PreparedPaymentDto> PrepareAsync(
        string ownerAccountId, string idempotencyKey, AuthorizePaymentRequest request,
        CancellationToken cancellationToken = default, Guid? assessmentId = null, Guid? approvalId = null)
    {
        if (!Guid.TryParse(idempotencyKey, out var parsedKey)) throw new ArgumentException("Idempotency-Key must be a GUID.");
        if (request is null || string.IsNullOrEmpty(request.AmountYocto) || request.AmountYocto.Length > 39 ||
            request.AmountYocto.Any(c => c < '0' || c > '9')) throw new ArgumentException("Invalid payment intent.");
        var intent = GatewayPaymentRequest.FromLegacy(request).Validate(DateTimeOffset.UtcNow);
        request = intent.ToLegacy();
        var config = await _contract.GetConfigAsync(cancellationToken);
        if (config.Owner != ownerAccountId) throw new UnauthorizedAccessException();
        var key = parsedKey.ToString("D");
        var existing = await FindExistingAsync(ownerAccountId, key, cancellationToken);
        if (existing is not null)
        {
            var recreated = RecreatePrepared(existing, request);
            if (existing.Status != "Prepared" || existing.SecurityAssessmentId is null)
                throw new GatewayRejectedException("INTENT_NOT_PREPARABLE");
            await _gateway.AuthorizePreparationAsync(ownerAccountId, intent, existing.SecurityAssessmentId,
                existing.SecurityApprovalId, existing.PaymentId, cancellationToken);
            return recreated;
        }
        var paymentId = Guid.NewGuid().ToString("N");
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var assessed = await _gateway.AuthorizePreparationAsync(ownerAccountId, intent, assessmentId, approvalId, paymentId, cancellationToken);
            var prepared = await _transactions.PrepareAuthorizationAsync(ownerAccountId, request, cancellationToken, paymentId);
            _db.PaymentRequests.Add(new PaymentRequestRecord {
                PaymentId = paymentId, IdempotencyKey = key, OwnerAccountId = ownerAccountId, ContractId = _near.ContractId,
                Recipient = request.Recipient, AmountYocto = request.AmountYocto, ExpiresAtNs = request.ExpiresAtNs, Purpose = request.Purpose,
                RiskScore = assessed.Risk.Score, RiskLevel = assessed.Risk.Decision == "WARN" ? "Medium" : "Low",
                RiskDecision = assessed.Risk.Decision, RiskReasonsJson = JsonSerializer.Serialize(assessed.Risk.ReasonCodes),
                SecurityAssessmentId = assessmentId ?? assessed.AssessmentId, SecurityApprovalId = approvalId, SecurityIntentHash = assessed.IntentHash
            });
            _db.AuditEvents.Add(new AuditEvent { EventType = "PaymentRiskApproved", ActorId = ownerAccountId, PaymentId = paymentId,
                DetailsJson = JsonSerializer.Serialize(new { assessed.AssessmentId, assessed.IntentHash, assessed.Risk.Decision }) });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return prepared;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _db.ChangeTracker.Clear();
            existing = await FindExistingAsync(ownerAccountId, key, cancellationToken);
            if (existing is null) throw;
            return await PrepareAsync(ownerAccountId, idempotencyKey, request, cancellationToken, assessmentId, approvalId);
        }
        catch (GatewayRejectedException ex)
        {
            _db.ChangeTracker.Clear();
            _db.AuditEvents.Add(new AuditEvent { EventType = "PaymentRiskRejected", ActorId = ownerAccountId,
                DetailsJson = JsonSerializer.Serialize(new { ex.Code, Decision = ex.Assessment?.Risk.Decision }) });
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TransactionVerificationDto> ReportTransactionAsync(
        string paymentId,
        string accountId,
        ReportTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentId) ||
            paymentId.Length > 64 ||
            string.IsNullOrWhiteSpace(request.TransactionHash) ||
            request.TransactionHash.Length > 128 ||
            request.Operation is not ("Authorization" or "Execution"))
        {
            throw new ArgumentException("Invalid transaction report.");
        }

        var payment = await _db.PaymentRequests
            .SingleOrDefaultAsync(
                x => x.PaymentId == paymentId &&
                     x.ContractId == _near.ContractId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Payment not found.");

        var config = await _contract.GetConfigAsync(
            cancellationToken);

        var expectedSigner = request.Operation == "Authorization"
            ? config.Owner
            : config.Agent;

        if (accountId != expectedSigner)
            throw new UnauthorizedAccessException(
                "Account is not authorized for this operation.");

        var verified = await _verifier.VerifyAsync(
            request.TransactionHash,
            accountId,
            paymentId,
            request.Operation,
            payment.Recipient,
            payment.AmountYocto,
            payment.ExpiresAtNs,
            cancellationToken);

        if (!verified)
            throw new InvalidOperationException(
                "Transaction does not match the payment intent.");

        var existing = await _db.PaymentTransactionAttempts
            .SingleOrDefaultAsync(
                x => x.TransactionHash == request.TransactionHash,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.PaymentRequestId != payment.Id ||
                existing.Operation != request.Operation ||
                existing.SignerAccountId != accountId)
            {
                throw new InvalidOperationException(
                    "Transaction hash belongs to another operation.");
            }

            return new TransactionVerificationDto(
                request.TransactionHash,
                paymentId,
                request.Operation,
                existing.Status);
        }

        _db.PaymentTransactionAttempts.Add(
            new PaymentTransactionAttempt
            {
                PaymentRequestId = payment.Id,
                Operation = request.Operation,
                SignerAccountId = accountId,
                TransactionHash = request.TransactionHash,
                Status = "Verified",
                VerifiedAtUtc = DateTime.UtcNow
            });

        _db.AuditEvents.Add(new AuditEvent
        {
            EventType = "TransactionVerified",
            ActorId = accountId,
            PaymentId = paymentId,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    request.Operation,
                    request.TransactionHash
                })
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Read the actual contract state after transaction verification.
        await SyncAsync(paymentId, cancellationToken);

        return new TransactionVerificationDto(
            request.TransactionHash,
            paymentId,
            request.Operation,
            "Verified");
    }


    public async Task<PaymentTrackingDto?> GetAsync(
        string paymentId,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.PaymentRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.PaymentId == paymentId &&
                     x.ContractId == _near.ContractId,
                cancellationToken);

        return record is null ? null : ToDto(record);
    }

    public async Task<PaymentTrackingDto?> SyncAsync(
        string paymentId,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.PaymentRequests
            .SingleOrDefaultAsync(
                x => x.PaymentId == paymentId &&
                     x.ContractId == _near.ContractId,
                cancellationToken);

        if (record is null)
            return null;

        var onChain = await _contract.GetPaymentAsync(
            paymentId,
            cancellationToken);

        if (onChain is null)
            return ToDto(record);

        // Do not reconcile unrelated or mismatched data.
        if (onChain.Recipient != record.Recipient ||
            onChain.AmountYocto != record.AmountYocto ||
            onChain.ExpiresAtNs != record.ExpiresAtNs)
        {
            throw new InvalidOperationException(
                "On-chain payment does not match recorded intent.");
        }

        var nextStatus = onChain.Status.ToString();

        if (record.Status != nextStatus)
        {
            record.Status = nextStatus;
            record.UpdatedAtUtc = DateTime.UtcNow;

            if (nextStatus is "Completed" or "Failed" or "Cancelled")
                record.CompletedAtUtc = DateTime.UtcNow;

            _db.AuditEvents.Add(new AuditEvent
            {
                EventType = "PaymentReconciled",
                PaymentId = paymentId,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(
                    new { Status = nextStatus })
            });

            await _db.SaveChangesAsync(cancellationToken);
        }

        return ToDto(record);
    }

    private Task<PaymentRequestRecord?> FindExistingAsync(
        string owner,
        string key,
        CancellationToken cancellationToken)
    {
        return _db.PaymentRequests.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OwnerAccountId == owner &&
                     x.ContractId == _near.ContractId &&
                     x.IdempotencyKey == key,
                cancellationToken);
    }


    private PreparedPaymentDto RecreatePrepared(
        PaymentRequestRecord record,
        AuthorizePaymentRequest request)
    {
        if (record.Recipient != request.Recipient ||
            record.AmountYocto != request.AmountYocto ||
            record.ExpiresAtNs != request.ExpiresAtNs ||
            record.Purpose != request.Purpose)
        {
            throw new InvalidOperationException(
                "Idempotency key was used for another payment.");
        }

        if (record.RiskDecision is not ("ALLOW" or "WARN"))
        {
            throw new InvalidOperationException(
                "Payment has no approved risk assessment.");
        }

        return new PreparedPaymentDto(
            record.PaymentId,
            new ContractCallDto(
                record.ContractId,
                "authorize_payment",
                new
                {
                    id = record.PaymentId,
                    recipient = record.Recipient,
                    amount_yocto = record.AmountYocto,
                    expires_at_ns =
                        record.ExpiresAtNs.ToString(
                            CultureInfo.InvariantCulture)
                },
                "30000000000000",
                "0"
            )
        );
    }


    private static PaymentTrackingDto ToDto(
        PaymentRequestRecord record) => new(
            record.PaymentId,
            record.Recipient,
            record.AmountYocto,
            record.Status,
            record.CreatedAtUtc,
            record.UpdatedAtUtc
        );
}
