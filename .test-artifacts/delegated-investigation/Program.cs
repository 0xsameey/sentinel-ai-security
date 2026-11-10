using System.Text.Json;
using System.Xml.Linq;
using System.Numerics;
using Npgsql;
var root=Directory.GetCurrentDirectory();
var id=XDocument.Load("src/Sentinel.Api/Sentinel.Api.csproj").Descendants("UserSecretsId").Single().Value;
var values=new Dictionary<string,string>();
void Walk(JsonElement e,string prefix=""){foreach(var p in e.EnumerateObject()){var k=prefix+p.Name;if(p.Value.ValueKind==JsonValueKind.Object)Walk(p.Value,k+":");else if(p.Value.ValueKind==JsonValueKind.String)values[k]=p.Value.GetString()!;}}
foreach(var file in new[]{"src/Sentinel.Api/appsettings.json","src/Sentinel.Api/appsettings.Development.json",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft/UserSecrets",id,"secrets.json")})if(File.Exists(file)){using var j=JsonDocument.Parse(File.ReadAllText(file));Walk(j.RootElement);}
string? Config(string k)=>Environment.GetEnvironmentVariable(k.Replace(":","__"))??values.GetValueOrDefault(k);
var signer=new Dictionary<string,string>();
var envFile="src/sentinel-signer/.env";
if(File.Exists(envFile))foreach(var line in File.ReadLines(envFile)){var i=line.IndexOf('=');if(i>0&&!line.TrimStart().StartsWith('#'))signer[line[..i].Trim()]=line[(i+1)..].Trim().Trim('"','\'');}
string? Signer(string k)=>Environment.GetEnvironmentVariable(k)??signer.GetValueOrDefault(k);
Console.WriteLine(JsonSerializer.Serialize(new{kind="config",dispatcherExpected=Config("DelegatedDispatcher:Url")?.TrimEnd('/') is "http://127.0.0.1:4301" or "http://127.0.0.1:4301/execute",dispatcherKeyConfigured=!string.IsNullOrEmpty(Config("DelegatedDispatcher:Key")),keyMatches=Config("DelegatedDispatcher:Key") is {} key && key==Signer("SENTINEL_DISPATCH_KEY"),delegatedMode=Signer("SENTINEL_PAYMENT_MODE")=="delegated",signerPrivateKeyConfigured=!string.IsNullOrEmpty(Signer("NEAR_AGENT_PRIVATE_KEY")),delegatedPublicPinConfigured=!string.IsNullOrEmpty(Config("SecurityRelease:DelegatedAgentPublicKey")),signerApiExpected=Signer("SENTINEL_API_URL")?.TrimEnd('/')=="https://localhost:7201"}));
var b=new NpgsqlConnectionStringBuilder(Config("ConnectionStrings:SentinelDb")){Timeout=5,Pooling=false,IncludeErrorDetail=false};
if(b.Host is not("localhost" or "127.0.0.1" or "::1"))throw new Exception("Loopback only");
await using var db=new NpgsqlConnection(b.ConnectionString);await db.OpenAsync();await using var tx=await db.BeginTransactionAsync();
await using(var ro=new NpgsqlCommand("SET TRANSACTION READ ONLY",db,tx))await ro.ExecuteNonQueryAsync();
await using(var cmd=new NpgsqlCommand("SELECT \"EventType\",\"CreatedAtUtc\",\"PaymentId\",\"DetailsJson\" FROM public.audit_events WHERE \"Id\"=@id OR \"DetailsJson\"::text LIKE @text ORDER BY \"CreatedAtUtc\"",db,tx)){
 cmd.Parameters.AddWithValue("id",Guid.Parse("d88fb189-497c-44aa-97ff-7f070ea9da41"));cmd.Parameters.AddWithValue("text","%d88fb189-497c-44aa-97ff-7f070ea9da41%");
 await using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())Console.WriteLine(JsonSerializer.Serialize(new{kind="targetRun",type=r.GetString(0),createdAt=r.GetDateTime(1),paymentId=r.IsDBNull(2)?null:r.GetString(2),details=r.IsDBNull(3)?null:r.GetString(3)}));
}
await using(var cmd=new NpgsqlCommand("SELECT p.\"Id\",p.\"DelegationId\",p.\"IntentJson\",p.\"Decision\",p.\"DecisionJson\",p.\"Status\",p.\"CreatedAtUtc\",p.\"PermitId\" IS NOT NULL,p.\"ConsumedAtUtc\" IS NOT NULL,p.\"TransactionHash\" IS NOT NULL,d.\"StateJson\",d.\"ObservedAtUtc\" FROM public.delegated_payments p LEFT JOIN public.delegations d ON d.\"Id\"=p.\"DelegationId\" AND d.\"ContractId\"=p.\"ContractId\" ORDER BY p.\"CreatedAtUtc\" DESC LIMIT 20",db,tx))
await using(var r=await cmd.ExecuteReaderAsync())while(await r.ReadAsync()){
 using var intent=JsonDocument.Parse(r.GetString(2));var i=intent.RootElement;var ns=i.GetProperty("ExpiresAtNs").GetString()!;var expires=DateTimeOffset.FromUnixTimeMilliseconds((long)(BigInteger.Parse(ns)/1000000));
 using var risk=JsonDocument.Parse(r.GetString(4));
 Console.WriteLine(JsonSerializer.Serialize(new{kind="payment",id=r.GetString(0),delegation=r.GetString(1),recipient=i.GetProperty("Recipient").GetString(),amountYocto=i.GetProperty("AmountYocto").GetString(),expiresAtNs=ns,expiresAtUtc=expires,createdAtUtc=r.GetDateTime(6),auditAtUtc=DateTimeOffset.UtcNow,expired=expires<=DateTimeOffset.UtcNow,decision=r.GetString(3),reasons=risk.RootElement.GetProperty("reasonCodes"),status=r.GetString(5),permitCreated=r.GetBoolean(7),permitConsumed=r.GetBoolean(8),hashReported=r.GetBoolean(9),delegationSnapshot=r.IsDBNull(10)?null:r.GetString(10),snapshotObserved=r.IsDBNull(11)?(DateTime?)null:r.GetDateTime(11)}));
}
await tx.RollbackAsync();

