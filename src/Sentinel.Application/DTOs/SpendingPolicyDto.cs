
namespace Sentinel.Application.DTOs;

public sealed record SpendingPolicyDto(
    string MaxPerTransactionYocto,
    string DailyLimitYocto
);

public sealed record DailyUsageDto(
    ulong DayIndex,
    string AuthorizedYocto,
    string ExecutedYocto,
    string ExecutingYocto
);

public sealed record RecipientStatusDto(
    string AccountId,
    bool IsAllowed
);
