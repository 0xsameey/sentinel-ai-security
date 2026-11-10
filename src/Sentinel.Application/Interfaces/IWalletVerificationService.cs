
using Sentinel.Application.DTOs;

namespace Sentinel.Application.Interfaces;

public interface IWalletVerificationService
{
    Task<string?> VerifyAsync(
        VerifyWalletRequest request,
        CancellationToken cancellationToken = default);
}
