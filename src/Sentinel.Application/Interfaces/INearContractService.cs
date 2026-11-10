using Sentinel.Application.DTOs;
using Sentinel.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sentinel.Application.Interfaces
{
    public interface INearContractService
    {
        Task<ContractConfigDto> GetConfigAsync(
            CancellationToken cancellationToken = default);

        Task<string> GetAvailableBalanceAsync(
            CancellationToken cancellationToken = default);

        Task<Payment?> GetPaymentAsync(
            string paymentId,
            CancellationToken cancellationToken = default);
    }
}
