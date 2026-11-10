
namespace Sentinel.Application.DTOs;

public sealed record UpdateSpendingPolicyRequest(
    string MaxPerTransactionYocto,
    string DailyLimitYocto
);

public sealed record UpdateRecipientRequest(
    bool Allowed
);

public sealed record ContractCallDto(
    string ReceiverId,
    string MethodName,
    object Args,
    string Gas,
    string Deposit,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Sentinel.Application.Security.ExecutionSecurityDto? Security = null
);
