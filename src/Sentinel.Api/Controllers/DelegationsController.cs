using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sentinel.Api.Authentication;
using Sentinel.Application.Security;
using Sentinel.Infrastructure.Security;

namespace Sentinel.Api.Controllers;

[ApiController]
[Route("api/v1/delegations")]
[Authorize(AuthenticationSchemes=JwtBearerDefaults.AuthenticationScheme+","+GatewayAgentHandler.SchemeName)]
[EnableRateLimiting("gateway")]
[RequestSizeLimit(8192)]
public sealed class DelegationsController(DelegatedSpendingService service) : ControllerBase {
    private string Actor=>User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct)=>Ok(await service.List(Actor,ct));
    [HttpGet("{id}")] public async Task<IActionResult> Inspect(string id,CancellationToken ct)=>Ok(await service.Inspect(Actor,id,ct));
    [HttpPost]
    [Authorize(AuthenticationSchemes=JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Create(CreateDelegationRequest r,CancellationToken ct)=>Ok(await service.Create(Actor,r,ct));
    [HttpPost("{id}/revoke")]
    [Authorize(AuthenticationSchemes=JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Revoke(string id,CancellationToken ct)=>Ok(await service.Revoke(Actor,id,ct));
    [HttpPost("proposals")]
    public async Task<IActionResult> Propose(DelegatedProposalRequest r,[FromHeader(Name="X-Sentinel-Agent-Run")]Guid? runId,CancellationToken ct)=>Ok(await service.Propose(Actor,r,ct,runId));
    [HttpGet("payments/{id}")]
    public async Task<IActionResult> Get(string id,CancellationToken ct)=>Ok(await service.Get(Actor,id,ct));
    [HttpPost("payments/{id}/reassess")]
    public async Task<IActionResult> Reassess(string id,CancellationToken ct)=>Ok(await service.Reassess(Actor,id,ct));
    [HttpPost("payments/{id}/approve")]
    [Authorize(AuthenticationSchemes=JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Approve(string id,ApproveAssessmentRequest r,CancellationToken ct) {await service.Approve(Actor,id,r,ct);return Ok(new{approved=true});}
    [HttpPost("payments/{id}/reconcile")]
    public async Task<IActionResult> Reconcile(string id,CancellationToken ct)=>Ok(await service.Reconcile(Actor,id,ct));
}

[ApiController]
[AllowAnonymous]
[Route("api/internal/agent/delegated-payments")]
[EnableRateLimiting("worker")]
public sealed class DelegatedWorkerController(DelegatedSpendingService service,AgentWorkerAuthenticator auth) : ControllerBase {
    private bool Authenticate() => auth.IsValid(Request.Headers["X-Sentinel-Worker-Key"].FirstOrDefault());
    [HttpPost("{id}/prepare-execution")]
    public async Task<IActionResult> Prepare(string id,CancellationToken ct) {if(!Authenticate())return Unauthorized();return Ok(await service.Prepare(id,ct));}
    [HttpPost("{id}/consume-permit")]
    public async Task<IActionResult> Consume(string id,ConsumePermitRequest r,CancellationToken ct) {if(!Authenticate())return Unauthorized();return Ok(await service.Consume(id,r,ct));}
    [HttpPost("{id}/report-transaction")]
    public async Task<IActionResult> Report(string id,Sentinel.Application.DTOs.ReportTransactionRequest r,CancellationToken ct) {
        if(!Authenticate())return Unauthorized();if(r.Operation!="Execution")throw new ArgumentException("Execution only.");await service.Report(id,r.TransactionHash,ct);return Ok(new{status="ReportedUnverified"});
    }
}
