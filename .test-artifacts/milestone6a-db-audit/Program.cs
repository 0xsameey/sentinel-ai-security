using System.Text.Json;
using System.Xml.Linq;
using Npgsql;
var root=Directory.GetCurrentDirectory();
var api=Path.Combine(root,"src/Sentinel.Api");
var id=XDocument.Load(Path.Combine(api,"Sentinel.Api.csproj")).Descendants("UserSecretsId").Single().Value;
var files=new[]{Path.Combine(api,"appsettings.json"),Path.Combine(api,"appsettings.Development.json"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft/UserSecrets",id,"secrets.json")};
var values=new Dictionary<string,string>();
void Walk(JsonElement e,string prefix="") {foreach(var p in e.EnumerateObject()){var key=prefix+p.Name;if(p.Value.ValueKind==JsonValueKind.Object)Walk(p.Value,key+":");else if(p.Value.ValueKind==JsonValueKind.String)values[key]=p.Value.GetString()!;}}
foreach(var file in files)if(File.Exists(file)){using var d=JsonDocument.Parse(File.ReadAllText(file));Walk(d.RootElement);}
foreach(var name in new[]{"SecurityRelease:AgentPublicKey","SecurityRelease:DelegatedAgentPublicKey","AgentRunner:ApiKey","DelegatedDispatcher:Key","AgentGateway:ApiKey","AgentWorker:ApiKey"}) {
 var value=Environment.GetEnvironmentVariable(name.Replace(":","__"))??values.GetValueOrDefault(name);
 Console.WriteLine(name+" configured: "+!string.IsNullOrWhiteSpace(value));
}
foreach(var name in new[]{"AgentRunner:Url","DelegatedDispatcher:Url","AgentDispatcher:Url"}) {
 var text=Environment.GetEnvironmentVariable(name.Replace(":","__"))??values.GetValueOrDefault(name);
 Console.WriteLine(name+" valid trusted URL: "+(Uri.TryCreate(text,UriKind.Absolute,out var u)&&u.UserInfo.Length==0&&u.Query.Length==0&&u.Fragment.Length==0&&(u.Scheme=="https"||u.Scheme=="http"&&u.IsLoopback)));
}
var contract=Environment.GetEnvironmentVariable("Near__ContractId")??values.GetValueOrDefault("Near:ContractId");Console.WriteLine("API contract matches audit target: "+(contract=="0xsameey-sentinel-v2.testnet"));
var connection=Environment.GetEnvironmentVariable("ConnectionStrings__SentinelDb")??values.GetValueOrDefault("ConnectionStrings:SentinelDb");
if(string.IsNullOrWhiteSpace(connection))throw new Exception("Database not configured");
var b=new NpgsqlConnectionStringBuilder(connection){Timeout=5,Pooling=false,IncludeErrorDetail=false};
if(b.Host is not("localhost" or "127.0.0.1" or "::1"))throw new Exception("Loopback audit only");
await using var db=new NpgsqlConnection(b.ConnectionString);await db.OpenAsync();await using var tx=await db.BeginTransactionAsync();
await using(var cmd=new NpgsqlCommand("SET TRANSACTION READ ONLY",db,tx))await cmd.ExecuteNonQueryAsync();
await using(var cmd=new NpgsqlCommand("SELECT table_name,column_name,data_type,is_nullable FROM information_schema.columns WHERE table_schema='public' AND table_name IN ('security_assessments','human_approvals','execution_permits','counterparty_blocklist','delegations','delegated_payments','payment_requests','payment_transaction_attempts') ORDER BY table_name,ordinal_position",db,tx))
await using(var r=await cmd.ExecuteReaderAsync())while(await r.ReadAsync())Console.WriteLine("COLUMN "+r.GetString(0)+"."+r.GetString(1)+" "+r.GetString(2)+" nullable="+r.GetString(3));
await tx.RollbackAsync();Console.WriteLine("Read-only metadata transaction finished; no writes.");
