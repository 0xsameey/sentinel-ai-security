
namespace Sentinel.Application.DTOs;


using System.Text.Json.Serialization;

public sealed record AuthorizePaymentRequest(
    string Recipient,
    string AmountYocto,

    [property: JsonNumberHandling(
        JsonNumberHandling.AllowReadingFromString)]
    ulong ExpiresAtNs,

    string Purpose
);

public sealed record PreparedPaymentDto(
    string PaymentId,
    ContractCallDto Transaction
);
