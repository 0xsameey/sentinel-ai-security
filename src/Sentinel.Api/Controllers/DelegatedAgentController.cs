using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sentinel.Application.Security;
using Sentinel.Infrastructure.Security;
using Sentinel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sentinel.Api.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DelegatedTaskRequest(string DelegationId,string Task,string Recipient,string MaxAmountYocto);

[ApiController]
[Route("api/v1/delegated-agent")]
[Authorize(AuthenticationSchemes=JwtBearerDefaults.AuthenticationScheme+","+Sentinel.Api.Authentication.GatewayAgentHandler.SchemeName)]
[EnableRateLimiting("gateway")]
[RequestSizeLimit(8192)]
public sealed class DelegatedAgentController(DelegatedSpendingService spending,IHttpClientFactory clients,IConfiguration config,
    SentinelDbContext db,TimeProvider clock,ILogger<DelegatedAgentController> logger) : ControllerBase {
    private string Actor=>User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();
    private static Uri Endpoint(string? text) {
        if(!Uri.TryCreate(text,UriKind.Absolute,out var u) || u.UserInfo.Length>0 || u.Query.Length>0 || u.Fragment.Length>0 ||
            !(u.Scheme=="https" || u.Scheme=="http" && u.IsLoopback)) throw new GatewayRejectedException("DELEGATED_SERVICE_UNCONFIGURED");
        return u;
    }
    [HttpPost("payments/{id}/dispatch")]
    public async Task<IActionResult> Dispatch(string id,CancellationToken ct) {
        await spending.Preflight(Actor,id,ct);
        var url=Endpoint(config["DelegatedDispatcher:Url"]);var key=config["DelegatedDispatcher:Key"];
        if(key is null || key.Length<32)throw new GatewayRejectedException("DELEGATED_DISPATCHER_UNCONFIGURED");
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(url,"execute"));
        request.Headers.Add("X-Sentinel-Dispatch-Key",key);request.Content=JsonContent.Create(new{paymentId=id});
        try {
            using var response=await clients.CreateClient("SentinelAgentDispatcher").SendAsync(request,ct);
            // Signer prose never becomes settlement evidence.
            logger.LogInformation("Delegated dispatch payment {PaymentId}: HTTP {StatusCode}; reconcile required",id,(int)response.StatusCode);
            if(!response.IsSuccessStatusCode) {
                string? signerCode=null;string? signerStage=null;
                try {
                    var error=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                    if(error.TryGetProperty("code",out var code) && code.GetString() is {} value &&
                        (new[]{"SIGNER_API_CONNECTION_REFUSED","SIGNER_API_TLS_UNTRUSTED","SIGNER_API_TRANSPORT_UNAVAILABLE","SIGNER_EXECUTION_UNCONFIRMED","SIGNER_API_HTTP_400","SIGNER_API_HTTP_401","SIGNER_API_HTTP_403","SIGNER_API_HTTP_409","SIGNER_API_HTTP_422","SIGNER_API_HTTP_502","SIGNER_API_HTTP_503"}).Contains(value)) signerCode=value;
                    if(error.TryGetProperty("stage",out var phase) && phase.GetString() is "PreparePermit" or "ConsumePermit" or "ReportTransaction" or "Signer")signerStage=phase.GetString();
                } catch(JsonException) { /* Never expose arbitrary signer bodies. */ }
                return StatusCode((int)response.StatusCode,new{paymentId=id,status="DispatchUnconfirmed",stage="Dispatch",signerCode,signerStage,reconciliationRequired=true});
            }
            return StatusCode((int)response.StatusCode,new{paymentId=id,status=response.IsSuccessStatusCode?"DispatchAccepted":"DispatchUnconfirmed",stage="Dispatch",reconciliationRequired=true});
        } catch(Exception ex) when(ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested) {
            logger.LogWarning("Delegated dispatch transport unconfirmed for payment {PaymentId}; reconcile before retry",id);
            return StatusCode(502,new{paymentId=id,status="DispatchUnconfirmed",code="DISPATCH_TRANSPORT_UNCONFIRMED",reconciliationRequired=true});
        }
    }
    [HttpPost("tasks")]
    [Authorize(AuthenticationSchemes=JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Run(DelegatedTaskRequest r,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct) {
        await spending.RequireOwner(Actor,ct);DelegatedSpendingService.Id(r.DelegationId);
        if(!Guid.TryParse(key,out var runId) || runId==Guid.Empty || string.IsNullOrWhiteSpace(r.Task) || r.Task.Length>2000)throw new ArgumentException("Invalid task.");
        new GatewayPaymentRequest(r.Recipient,r.MaxAmountYocto,(new System.Numerics.BigInteger(clock.GetUtcNow().AddMinutes(15).ToUnixTimeMilliseconds())*1_000_000).ToString(),"Task ceiling").Validate(clock.GetUtcNow());
        var url=Endpoint(config["AgentRunner:Url"]);var serviceKey=config["AgentRunner:ApiKey"];
        if(serviceKey is null || serviceKey.Length<32)throw new GatewayRejectedException("AGENT_RUNNER_UNCONFIGURED");
        var existing=await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==runId,ct);
        if(existing is not null) {
            if(existing.ActorId!=Actor || existing.EventType!="DelegatedAgentRunStarted")throw new GatewayRejectedException("AGENT_RUN_ALREADY_CLAIMED");
            var previous=JsonSerializer.Deserialize<JsonElement>(existing.DetailsJson!);
            if(previous.GetProperty("DelegationId").GetString()!=r.DelegationId || previous.GetProperty("Recipient").GetString()!=r.Recipient || previous.GetProperty("MaxAmountYocto").GetString()!=r.MaxAmountYocto ||
                !previous.TryGetProperty("taskHash",out var hash) || hash.GetString()!=GatewayPaymentRequest.HashText(r.Task))throw new GatewayRejectedException("AGENT_RUN_ALREADY_CLAIMED");
            return await BoundResult(runId,"DispatchUnconfirmed",ct) ?? Conflict(new{runId,code="AGENT_RUN_ALREADY_CLAIMED",reconciliationRequired=true});
        }
        var started=clock.GetUtcNow().UtcDateTime;
        db.AuditEvents.Add(new(){Id=runId,EventType="DelegatedAgentRunStarted",ActorId=Actor,CreatedAtUtc=started,
            DetailsJson=JsonSerializer.Serialize(new{r.DelegationId,r.Recipient,r.MaxAmountYocto,taskHash=GatewayPaymentRequest.HashText(r.Task)})});
        try { await db.SaveChangesAsync(ct); } // Unique run ID is a permanent claim.
        catch(DbUpdateException ex) when(ex.InnerException is Npgsql.PostgresException pg && pg.SqlState==Npgsql.PostgresErrorCodes.UniqueViolation) {
            db.ChangeTracker.Clear();
            return Conflict(new{runId,code="AGENT_RUN_ALREADY_CLAIMED",reconciliationRequired=true});
        }
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(url,"delegated-tasks"));
        request.Headers.Add("X-Sentinel-Runner-Key",serviceKey);
        request.Content=JsonContent.Create(new{runId,task=r.Task,recipient=r.Recipient,maxAmountYocto=r.MaxAmountYocto,delegationId=r.DelegationId});
        try {
        using var response=await clients.CreateClient("SentinelAgentRunner").SendAsync(request,ct);
        if(!response.IsSuccessStatusCode) {
            logger.LogWarning("Delegated runner run {RunId} returned HTTP {StatusCode}; checking persisted proposal",runId,(int)response.StatusCode);
            return await BoundResult(runId,"DispatchUnconfirmed",ct) ?? StatusCode(502,new{runId,code="AGENT_RUN_UNAVAILABLE",stage="Runner",proposalCreated=false});
        }
        var result=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if(result.GetProperty("runId").GetGuid()!=runId)throw new GatewayRejectedException("AGENT_RESPONSE_INVALID");
        var binding=await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==AgentRunBinding.Id(runId) && x.ActorId==Actor && x.EventType=="DelegatedAgentProposalBound",ct);
        if(binding?.PaymentId!=result.GetProperty("paymentId").GetString())throw new GatewayRejectedException("AGENT_RESPONSE_INVALID");
        var dispatch=result.TryGetProperty("dispatchStatus",out var state) && state.GetString() is "DispatchAccepted" or "NotDispatched" ? state.GetString()! : "DispatchUnconfirmed";
        return await BoundResult(runId,dispatch,ct) ?? throw new GatewayRejectedException("AGENT_RESPONSE_INVALID");
        } catch(Exception ex) when(ex is HttpRequestException or JsonException or InvalidOperationException or FormatException || ex is OperationCanceledException && !ct.IsCancellationRequested) {
            logger.LogWarning("Delegated runner transport unconfirmed for run {RunId}; checking persisted proposal",runId);
            return await BoundResult(runId,"DispatchUnconfirmed",ct) ?? StatusCode(502,new{runId,code="AGENT_RUN_UNAVAILABLE",stage="Runner",proposalCreated=false});
        }
    }

    private async Task<IActionResult?> BoundResult(Guid runId,string dispatchStatus,CancellationToken ct) {
        var binding=await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==AgentRunBinding.Id(runId) && x.ActorId==Actor && x.EventType=="DelegatedAgentProposalBound",ct);
        if(binding?.PaymentId is null)return null;
        var payment=await spending.Get(Actor,binding.PaymentId,ct);
        return Ok(new{runId,payment,proposalCreated=true,dispatchStatus,reconciliationRequired=dispatchStatus!="NotDispatched",
            status=payment.Decision=="BLOCK"?"BLOCKED":payment.Decision=="WARN"?"HUMAN_APPROVAL_REQUIRED":dispatchStatus=="DispatchUnconfirmed"?"PROPOSAL_CREATED_DISPATCH_UNCONFIRMED":"AUTONOMOUS_PROPOSAL"});
    }

    [HttpGet("tasks/{runId:guid}")]
    [Authorize(AuthenticationSchemes=JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> GetRun(Guid runId,CancellationToken ct) {
        await spending.RequireOwner(Actor,ct);
        return await BoundResult(runId,"DispatchUnconfirmed",ct) ?? NotFound(new{runId,code="NO_BOUND_PROPOSAL"});
    }
}
