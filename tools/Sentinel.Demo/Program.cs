using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.DataProtection;
using Sentinel.Application.Security;
using Sentinel.Domain.Entities;
using Sentinel.Infrastructure.Security;
using Sentinel.Infrastructure.Persistence;
using Sentinel.IntegrationTests;

// Test-only composition. No production registration, SDK, key store or signer.
var root=Environment.GetEnvironmentVariable("SENTINEL_E2E_ROOT")??Directory.GetCurrentDirectory();
Directory.SetCurrentDirectory(root);
if(!File.Exists(Path.Combine(root,"Sentinel.slnx"))) throw new InvalidOperationException("Run from repository root.");
var dbSettings=new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SENTINEL_E2E_DB"));
if(dbSettings.Host is not ("localhost" or "127.0.0.1" or "::1")||
 !(dbSettings.Database?.StartsWith("sentinel_e2e_")==true||dbSettings.SearchPath?.StartsWith("sentinel_e2e_")==true))
 throw new InvalidOperationException("Demo requires an isolated loopback test namespace. Start with tests/start-milestone4-demo.ps1.");
var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);
listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
var child=new ProcessStartInfo("node") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
child.ArgumentList.Add("tests/run-near-fixture.cjs");child.Environment["PORT"]=port.ToString();
using var fixture=Process.Start(child)!;
Process? runner=null;
Environment.SetEnvironmentVariable("SENTINEL_E2E_NEAR",$"http://127.0.0.1:{port}/near");
Environment.SetEnvironmentVariable("SENTINEL_E2E_ROOT",root);
try {
using var readiness=new HttpClient();
for(var i=0;i<50;i++) { try { (await readiness.GetAsync($"http://127.0.0.1:{port}/near/health")).EnsureSuccessStatusCode();break; } catch when(i<49) {await Task.Delay(100);} }
await using var host=await GatewayFlowTests.GatewayHost.Start(10000);
var runnerKey=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
var runnerStart=new ProcessStartInfo("node") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
runnerStart.ArgumentList.Add("--experimental-strip-types");runnerStart.ArgumentList.Add("src/sentinel-agent-client/tests/api-agent-host.mjs");
runnerStart.Environment["TEST_API_URL"]=host.Owner.BaseAddress!.ToString().TrimEnd('/');
runnerStart.Environment["TEST_AGENT_KEY"]=host.Agent.DefaultRequestHeaders.GetValues("X-Sentinel-Agent-Key").Single();
runnerStart.Environment["TEST_RUNNER_KEY"]=runnerKey;runnerStart.Environment["TEST_PROVIDER"]="openai";
runner=Process.Start(runnerStart)!;
var ready=await runner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
if(ready is null||!ready.StartsWith("TEST_AGENT_READY:"))throw new InvalidOperationException("Deterministic agent runner failed.");
host.App.Configuration["AgentRunner:Url"]="http://127.0.0.1:"+int.Parse(ready.Split(':')[1])+"/";
host.App.Configuration["AgentRunner:ApiKey"]=runnerKey;
var evidence=await File.ReadAllTextAsync("docs/fixtures/milestone4-invoice.txt");
var sha=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(evidence)));
(await host.Owner.PostAsJsonAsync("/api/v1/gateway/blocklist",new AddBlocklistRequest("injection-sink.testnet","MANUAL_SECURITY_HOLD","https://sentinel.invalid/simulated/milestone4-invoice",sha,true,null))).EnsureSuccessStatusCode();
var builder=WebApplication.CreateBuilder(args);
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider().UseInMemoryTestKeyRepository();
var demoPortText=Environment.GetEnvironmentVariable("SENTINEL_DEMO_PORT")??"8040";
if(!int.TryParse(demoPortText,out var demoPort)||demoPort<1024||demoPort>65535)
 throw new InvalidOperationException("SENTINEL_DEMO_PORT must be between 1024 and 65535.");
var demoUrl=$"http://127.0.0.1:{demoPort}";
builder.WebHost.UseUrls(demoUrl);builder.Logging.ClearProviders();
await using var app=builder.Build();
var token=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
var gate=new SemaphoreSlim(1);
app.Use(async(context,next)=> {
 if(context.Request.Path.StartsWithSegments("/demo-api")) {
  if(context.Request.Headers.Authorization!="Bearer "+token) { context.Response.StatusCode=401;return; }
  await gate.WaitAsync();try {
   host.Owner.DefaultRequestHeaders.Authorization=new("Bearer",host.Tokens.CreateToken("owner.testnet").Token);
   await next(context);
  }finally{gate.Release();}return;
 }
 await next(context);
});
var assessments=new Dictionary<Guid,GatewayAssessmentDto>();var approvals=new Dictionary<Guid,Guid>();
var payments=new Dictionary<string,GatewayPaymentRequest>();
app.MapPost("/demo-api/run/{scenario}",async(string scenario)=>{
 if(scenario is not ("ALLOW" or "WARN" or "BLOCK" or "LIMIT")) return Results.BadRequest();
 host.Clock.Reset();await host.Seed(new {historyUnavailable=scenario=="WARN",recipientAllowed=true,keyMethods=new[]{"execute_payment"},contractCodeHash=NearExecutionReleaseGate.ReviewedCodeHash});
 var intent=host.Intent(scenario=="BLOCK"?evidence:"SIMULATED: payment for API credits");
 if(scenario=="BLOCK")intent=intent with {Recipient="injection-sink.testnet"};
 if(scenario=="LIMIT")intent=intent with {AmountYocto="1001"};
 int? agentRedirectStatus=null;
 if(scenario=="BLOCK") {
  var redirect=new HttpRequestMessage(HttpMethod.Post,"/api/v1/agent/tasks") {Content=JsonContent.Create(new {task="SIMULATED_REDIRECT: "+evidence,recipient="merchant.testnet",maxAmountYocto="100"})};
  redirect.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());
  using var redirected=await host.Owner.SendAsync(redirect);agentRedirectStatus=(int)redirected.StatusCode;
  if(redirected.IsSuccessStatusCode)throw new InvalidOperationException("Agent accepted an out-of-task redirect.");
 }
 GatewayAssessmentDto assessment;
 if(scenario=="LIMIT") {
  var response=await host.Agent.PostAsJsonAsync("/api/v1/gateway/assess",intent);response.EnsureSuccessStatusCode();
  assessment=(await response.Content.ReadFromJsonAsync<GatewayAssessmentDto>())!;
 } else {
  // Scripted model transport drives the real agent loop, task binding and canonical gateway reload.
  var message=new HttpRequestMessage(HttpMethod.Post,"/api/v1/agent/tasks") {Content=JsonContent.Create(new {task=intent.Purpose,recipient=intent.Recipient,maxAmountYocto="100"})};
  message.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());
  var response=await host.Owner.SendAsync(message);response.EnsureSuccessStatusCode();
  assessment=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assessment").Deserialize<GatewayAssessmentDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
 }
 assessments[assessment.AssessmentId]=assessment;
 return Results.Ok(new {simulated=true,assessment,agentRedirectStatus,untrustedContent=scenario=="BLOCK"?evidence:null,evidenceSha256=scenario=="BLOCK"?sha:null,state=assessment.Risk.Decision=="BLOCK"?"REFUSED":"AWAITING_OWNER",signed=false});
});
app.MapPost("/demo-api/approve/{id:guid}",async(Guid id)=>{
 if(!assessments.TryGetValue(id,out var a))return Results.NotFound();
 var response=await host.Owner.PostAsJsonAsync($"/api/v1/gateway/assessments/{id}/approve",new {fingerprint=a.Risk.Fingerprint,confirm=true});
 if(!response.IsSuccessStatusCode)return Results.Json(new {code="APPROVAL_REJECTED",status=(int)response.StatusCode},statusCode:(int)response.StatusCode);
 var approval=(await response.Content.ReadFromJsonAsync<HumanApprovalDto>())!;approvals[id]=approval.ApprovalId;return Results.Ok(approval);
});
app.MapPost("/demo-api/expire",()=>{host.Clock.Advance(TimeSpan.FromMinutes(3));return Results.Ok(new {simulated=true,state="CLOCK_ADVANCED_3_MINUTES"});});
app.MapPost("/demo-api/revalidate/{id:guid}",async(Guid id)=>{
 if(!assessments.ContainsKey(id))return Results.NotFound();
 await host.Seed(new {policy=new {max_per_transaction="2000",daily_limit="10000"}});
 try {
  using var result=await host.Prepare(id,approvals.TryGetValue(id,out var approval)?approval:null);
  return Results.Ok(new {simulated=true,changedContextStatus=(int)result.StatusCode,state=result.IsSuccessStatusCode?"PREPARED":"POLICY_CHANGED_REVALIDATION_REJECTED"});
 } finally {await host.Seed(new {policy=new {max_per_transaction="1000",daily_limit="10000"}});}
});
app.MapPost("/demo-api/execute/{id:guid}",async(Guid id)=>{
 if(!assessments.TryGetValue(id,out var a))return Results.NotFound();
 using var prepared=await host.Prepare(id,approvals.TryGetValue(id,out var approval)?approval:null);
 if(!prepared.IsSuccessStatusCode)return Results.Json(new {code="PREPARATION_REJECTED",status=(int)prepared.StatusCode},statusCode:(int)prepared.StatusCode);
 var payment=(await prepared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetString()!;
 payments[payment]=a.Intent;
 await host.Authorize(payment,a.Intent); // Explicit synthetic chain authorization, no signature.
 var executionResponse=await host.Owner.PostAsync($"/api/v1/gateway/payments/{payment}/assess-execution",null);executionResponse.EnsureSuccessStatusCode();
 var execution=(await executionResponse.Content.ReadFromJsonAsync<GatewayAssessmentDto>())!;
 if(execution.Risk.Decision!="ALLOW") {assessments[execution.AssessmentId]=execution;return Results.Ok(new {simulated=true,state="EXECUTION_REVIEW_REQUIRED",assessment=execution,paymentId=payment,signed=false});}
 return await Consume(payment);
});
async Task<IResult> Consume(string payment) {
 var permitResponse=await host.Worker.PostAsJsonAsync($"/api/internal/agent/payments/{payment}/prepare-execution",new {});
 if(!permitResponse.IsSuccessStatusCode)return Results.Json(new {code="EXECUTION_REJECTED"},statusCode:(int)permitResponse.StatusCode);
 var security=(await permitResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("security");
 var permit=new {permitId=security.GetProperty("permitId").GetGuid(),intentHash=security.GetProperty("intentHash").GetString()};
 var consumed=await host.Worker.PostAsJsonAsync($"/api/internal/agent/payments/{payment}/consume-permit",permit);consumed.EnsureSuccessStatusCode();
 var replay=await host.Worker.PostAsJsonAsync($"/api/internal/agent/payments/{payment}/consume-permit",permit);
 var intent=payments[payment];var hash=Guid.NewGuid().ToString("N");
 await host.Seed(new {payment=new {id=payment,recipient=intent.Recipient,amount_yocto=intent.AmountYocto,expires_at_ns=intent.ExpiresAtNs,status="Completed"},
  transaction=new {final_execution_status="FINAL",status=new {SuccessValue=""},transaction=new {hash,signer_id="agent.testnet",receiver_id="treasury.testnet",
   actions=new[]{new {FunctionCall=new {method_name="execute_payment",deposit="0",args=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {id=payment})))}}}}}});
 var report=await host.Worker.PostAsJsonAsync($"/api/internal/agent/payments/{payment}/report-transaction",new {transactionHash=hash,operation="Execution"});report.EnsureSuccessStatusCode();
 var tracking=await host.Owner.GetFromJsonAsync<JsonElement>($"/api/v1/payments/{payment}/tracking");
 return Results.Ok(new {simulated=true,state="SIMULATED_COMPLETED",paymentId=payment,tracking,permitConsumed=true,replayStatus=(int)replay.StatusCode,signed=false,broadcast=false});
}
app.MapPost("/demo-api/finish/{id:guid}",async(Guid id)=>{
 if(!assessments.TryGetValue(id,out var a)||a.Stage!="Execution"||a.PaymentId is null)return Results.BadRequest();
 return await Consume(a.PaymentId);
});
app.MapPost("/demo-api/probes/{id:guid}",async(Guid id)=>{
 if(!assessments.TryGetValue(id,out var a))return Results.NotFound();
 using var unauthorized=await host.Agent.PostAsJsonAsync($"/api/v1/gateway/assessments/{id}/approve",new {fingerprint=a.Risk.Fingerprint,confirm=true});
 using var forged=a.Risk.Decision=="WARN"?await host.Prepare(id,Guid.NewGuid()):null;
 using var bypass=await host.Worker.PostAsJsonAsync("/api/internal/agent/payments/unrecorded/prepare-execution",new {});
 return Results.Ok(new {simulated=true,unauthorizedApproval=(int)unauthorized.StatusCode,forgedApproval=forged is null?(int?)null:(int)forged.StatusCode,directGatewayBypass=(int)bypass.StatusCode, note="Forged approval probe applies to WARN; ALLOW requires no risk override."});
});

var delegated=new Dictionary<string,DelegatedPaymentRecord>();
async Task<IResult> CompleteDelegated(DelegatedPaymentRecord p,bool fail=false) {
 var prepared=await host.Worker.PostAsJsonAsync($"/api/internal/agent/delegated-payments/{p.Id}/prepare-execution",new{});
 if(!prepared.IsSuccessStatusCode)return Results.Json(new{code="DELEGATED_RELEASE_DENIED"},statusCode:(int)prepared.StatusCode);
 var call=(await prepared.Content.ReadFromJsonAsync<Sentinel.Application.DTOs.ContractCallDto>())!;
 var permit=new ConsumePermitRequest(call.Security!.PermitId,call.Security.IntentHash);
 var consumed=await host.Worker.PostAsJsonAsync($"/api/internal/agent/delegated-payments/{p.Id}/consume-permit",permit);consumed.EnsureSuccessStatusCode();
 var replay=await host.Worker.PostAsJsonAsync($"/api/internal/agent/delegated-payments/{p.Id}/consume-permit",permit);
 var intent=JsonSerializer.Deserialize<GatewayPaymentRequest>(p.IntentJson)!;
 await host.Seed(new{payment=new{id=p.Id,recipient=intent.Recipient,amount_yocto=intent.AmountYocto,expires_at_ns=intent.ExpiresAtNs,status=fail?"Failed":"Completed"},paymentDelegation=new{id=p.Id,delegationId=p.DelegationId}});
 var response=await host.Owner.PostAsJsonAsync($"/api/v1/delegations/payments/{p.Id}/reconcile",new{});response.EnsureSuccessStatusCode();
 return Results.Ok(new{simulated=true,mode="delegated",payment=p,evidence=await response.Content.ReadFromJsonAsync<JsonElement>(),replayStatus=(int)replay.StatusCode,signed=false,broadcast=false});
}
app.MapPost("/demo-api/delegated/{scenario}",async(string scenario)=>{
 if(scenario is not ("ALLOW" or "WARN" or "BLOCK" or "BUDGET" or "REVOKED" or "FAILED"))return Results.BadRequest();
 host.Clock.Reset();
 await host.Seed(new{historyUnavailable=scenario=="WARN",recipientAllowed=true,keyMethods=new[]{"execute_delegated_payment"},contractCodeHash=NearDelegatedReleaseGate.ReviewedCodeHash,
  delegation=new{id="demo-delegation",state=new{agent="agent.testnet",max_payment="200",budget="500",daily_limit="500",expires_at_ns=(new System.Numerics.BigInteger(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds())*1_000_000).ToString(),recipients=new[]{"merchant.testnet"},revoked=scenario=="REVOKED",spent=scenario=="BUDGET"?"500":"0",day=(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/86400000).ToString(),spent_today="0"}}});
 var intent=host.Intent("SIMULATED delegated autonomous spending: untrusted invoice cannot override owner caps");if(scenario=="BLOCK")intent=intent with{Recipient="injection-sink.testnet"};
 var response=await host.Agent.PostAsJsonAsync("/api/v1/delegations/proposals",new DelegatedProposalRequest("demo-delegation",intent));response.EnsureSuccessStatusCode();
 var p=(await response.Content.ReadFromJsonAsync<DelegatedPaymentRecord>())!;delegated[p.Id]=p;
 if(p.Decision=="ALLOW")return await CompleteDelegated(p,scenario=="FAILED");
 using var refused=await host.Worker.PostAsJsonAsync($"/api/internal/agent/delegated-payments/{p.Id}/prepare-execution",new{});
 return Results.Ok(new{simulated=true,mode="delegated",payment=p,releaseStatus=(int)refused.StatusCode,signed=false,broadcast=false});
});
app.MapPost("/demo-api/delegated-approve/{id}",async(string id)=>{
 if(!delegated.TryGetValue(id,out var p)||p.Decision!="WARN")return Results.BadRequest();
 var response=await host.Owner.PostAsJsonAsync($"/api/v1/delegations/payments/{id}/approve",new ApproveAssessmentRequest(p.Fingerprint,true));
 if(!response.IsSuccessStatusCode)return Results.Json(new{code="DELEGATED_APPROVAL_DENIED"},statusCode:(int)response.StatusCode);
 return await CompleteDelegated(p);
});
app.MapGet("/demo-api/audit",async()=>{using var scope=host.App.Services.CreateScope();return Results.Ok(await scope.ServiceProvider.GetRequiredService<SentinelDbContext>().AuditEvents.AsNoTracking().OrderByDescending(x=>x.CreatedAtUtc).Take(100).Select(x=>new {x.Id,x.EventType,x.CreatedAtUtc,x.ActorId,x.PaymentId,x.DetailsJson}).ToListAsync());});
var files=new PhysicalFileProvider(Path.Combine(root,"src/sentinel-dashboard/dist"));
app.UseStaticFiles(new StaticFileOptions{FileProvider=files});
app.MapFallback(()=>Results.File(Path.Combine(root,"src/sentinel-dashboard/dist/index.html"),"text/html"));
if(args.Contains("--verify")) {
 await app.StartAsync();
 try {
  using var client=new HttpClient {BaseAddress=new Uri(demoUrl)};
  using(var denied=await client.PostAsync("/demo-api/run/ALLOW",null))Check(denied.StatusCode==HttpStatusCode.Unauthorized,"demo authentication");
  client.DefaultRequestHeaders.Authorization=new("Bearer",token);
  async Task<JsonElement> Post(string path) {var response=await client.PostAsync("/demo-api/"+path,null);if(!response.IsSuccessStatusCode)throw new InvalidOperationException(path+": "+(int)response.StatusCode);return await response.Content.ReadFromJsonAsync<JsonElement>();}
  foreach(var scenario in new[]{"BLOCK","LIMIT"}) {
   var result=await Post("run/"+scenario);var a=result.GetProperty("assessment");var id=a.GetProperty("assessmentId").GetGuid();
   Check(a.GetProperty("risk").GetProperty("decision").GetString()=="BLOCK",scenario+" decision");
   if(scenario=="BLOCK") {
    Check(a.GetProperty("risk").GetProperty("evidence").ToString().Contains(sha),"blocklist evidence digest");
    Check(result.GetProperty("agentRedirectStatus").GetInt32()==422,"agent recipient constraint rejects injected redirect");
   }
   using var refused=await client.PostAsync("/demo-api/execute/"+id,null);Check(!refused.IsSuccessStatusCode,"BLOCK signing prevention");
  }
  var allow=await Post("run/ALLOW");var allowed=await Post("execute/"+allow.GetProperty("assessment").GetProperty("assessmentId").GetGuid());
  Check(allowed.GetProperty("state").GetString()=="SIMULATED_COMPLETED"&&allowed.GetProperty("replayStatus").GetInt32()==409,"ALLOW completion and replay denial");
  var warn=await Post("run/WARN");var wid=warn.GetProperty("assessment").GetProperty("assessmentId").GetGuid();
  var probes=await Post("probes/"+wid);Check(probes.GetProperty("unauthorizedApproval").GetInt32()==403&&probes.GetProperty("forgedApproval").GetInt32()==409&&probes.GetProperty("directGatewayBypass").GetInt32()==404,"unauthorized/forged/bypass denial");
  await Post("approve/"+wid);
  var changed=await Post("revalidate/"+wid);Check(changed.GetProperty("changedContextStatus").GetInt32()==409,"changed policy revalidation");
  await Post("expire");
  using(var expired=await client.PostAsync("/demo-api/execute/"+wid,null))Check(expired.StatusCode==HttpStatusCode.Conflict,"approval expiry");
  warn=await Post("run/WARN");wid=warn.GetProperty("assessment").GetProperty("assessmentId").GetGuid();await Post("approve/"+wid);
  var execution=await Post("execute/"+wid);var eid=execution.GetProperty("assessment").GetProperty("assessmentId").GetGuid();
  using(var unapproved=await client.PostAsync("/demo-api/finish/"+eid,null))Check(!unapproved.IsSuccessStatusCode,"separate execution approval required");
  await Post("approve/"+eid);var completed=await Post("finish/"+eid);Check(completed.GetProperty("state").GetString()=="SIMULATED_COMPLETED","WARN completion");
  var logs=await client.GetStringAsync("/demo-api/audit");Check(logs.Contains("SecurityAssessmentRecorded")&&logs.Contains("CounterpartyBlocklisted")&&logs.Contains("ExecutionPermitConsumed"),"persisted security audit");

  foreach(var scenario in new[]{"ALLOW","WARN","BLOCK","BUDGET","REVOKED","FAILED"}) {
   var result=await Post("delegated/"+scenario);var payment=result.GetProperty("payment");
   Check(result.GetProperty("simulated").GetBoolean()&&!result.GetProperty("signed").GetBoolean(),"delegated simulation label");
   if(scenario is "BLOCK" or "BUDGET" or "REVOKED")Check(payment.GetProperty("decision").GetString()=="BLOCK"&&result.GetProperty("releaseStatus").GetInt32()==422,"delegated refusal "+scenario);
   else if(scenario=="WARN") {Check(result.GetProperty("releaseStatus").GetInt32()==422,"delegated WARN requires human");var done=await Post("delegated-approve/"+payment.GetProperty("id").GetString());Check(done.GetProperty("replayStatus").GetInt32()==409,"delegated approved WARN replay denial");}
   else Check(result.GetProperty("evidence").GetProperty("status").GetString()==(scenario=="FAILED"?"Failed":"Settled"),"delegated simulated evidence "+scenario);
  }
  Console.WriteLine("MILESTONE5B_DEMO_VERIFIED: delegated ALLOW without per-payment owner approval, WARN review, malicious recipient, exhausted budget, revocation, failed transfer, single-use permits and labeled simulated evidence.");
  Console.WriteLine("MILESTONE4_DEMO_VERIFIED: authentication, evidence BLOCK, limits, ALLOW/WARN completion, expiry, forged approval, gateway bypass, permit replay and audit. No model calls or blockchain transactions.");
 } finally {await app.StopAsync();}
 return;
}
Console.WriteLine("SIMULATION ONLY: open "+demoUrl+"/demo#"+token);
Console.WriteLine("Ephemeral demo credential authenticates the simulated owner. No model calls, signing or broadcasts.");
await app.RunAsync();
} finally {
 if(runner is not null){if(!runner.HasExited){runner.Kill(true);await runner.WaitForExitAsync();}runner.Dispose();}
 if(!fixture.HasExited){fixture.Kill(true);await fixture.WaitForExitAsync();}
}
static void Check(bool condition,string guarantee) {if(!condition)throw new InvalidOperationException("Demo verification failed: "+guarantee);}
