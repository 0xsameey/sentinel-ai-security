
using Microsoft.AspNetCore.Mvc;
using Sentinel.Application.Interfaces;

namespace Sentinel.Api.Controllers;

[ApiController]
[Route("api/v1/contract")]
public sealed class ContractController : ControllerBase
{
    private readonly INearContractService _contractService;

    public ContractController(
        INearContractService contractService)
    {
        _contractService = contractService;
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(
        CancellationToken cancellationToken)
    {
        var config = await _contractService
            .GetConfigAsync(cancellationToken);

        return Ok(config);
    }

    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance(
        CancellationToken cancellationToken)
    {
        var balance = await _contractService
            .GetAvailableBalanceAsync(cancellationToken);

        return Ok(new
        {
            availableBalanceYocto = balance
        });
    }
}
