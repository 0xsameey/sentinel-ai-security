using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Text.Json;
using Npgsql;

// No connection strings, passwords, tokens or secret values are written to output.
var root = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root, "Sentinel.slnx"))) throw new InvalidOperationException("Run from repository root.");
var mode = args.FirstOrDefault() ?? "pg-inspect";
if (mode is not ("pg-inspect" or "pg-e2e" or "pg-evidence" or "pg-demo" or "pg-startup-check")) throw new ArgumentException("Use pg-startup-check, pg-inspect, pg-e2e, pg-demo or pg-evidence TEST_SCHEMA.");
var api = Path.Combine(root, "src/Sentinel.Api");
var secretsId = XDocument.Load(Path.Combine(api, "Sentinel.Api.csproj")).Descendants("UserSecretsId").Single().Value;
var secretsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft/UserSecrets", secretsId, "secrets.json");
string? connection = null;
foreach (var file in new[] { Path.Combine(api, "appsettings.json"), Path.Combine(api, "appsettings.Development.json"), secretsFile })
{
    if (!File.Exists(file)) continue;
    using var document = JsonDocument.Parse(File.ReadAllText(file));
    var json = document.RootElement;
    if (json.TryGetProperty("ConnectionStrings", out var strings) && strings.TryGetProperty("SentinelDb", out var value) && !string.IsNullOrWhiteSpace(value.GetString())) connection = value.GetString();
    if (json.TryGetProperty("ConnectionStrings:SentinelDb", out var flat) && !string.IsNullOrWhiteSpace(flat.GetString())) connection = flat.GetString();
}
connection = Environment.GetEnvironmentVariable("ConnectionStrings__SentinelDb") ?? connection;
try
{
    if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Local database connection not configured.");
    var settings = new NpgsqlConnectionStringBuilder(connection) { Timeout = 5, Pooling = false, IncludeErrorDetail = false };
    if (settings.Host is not ("localhost" or "127.0.0.1" or "::1"))
        throw new InvalidOperationException("Only loopback PostgreSQL may be inspected or used for local tests.");
    await using var existing = new NpgsqlConnection(settings.ConnectionString);
    await existing.OpenAsync();
    if (mode == "pg-evidence")
    {
        var testSchema = args.ElementAtOrDefault(1) ?? "";
        if (!Regex.IsMatch(testSchema, "^sentinel_e2e_[a-z0-9_]+$")) throw new InvalidOperationException("Only test schemas may be inspected.");
        await using (var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema=@schema", existing))
        {
            command.Parameters.AddWithValue("schema", testSchema);
            Console.WriteLine("Test schema table count: " + await command.ExecuteScalarAsync());
        }
        foreach (var table in new[] { "payment_requests", "payment_transaction_attempts", "audit_events", "__EFMigrationsHistory" })
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{testSchema}\".\"{table}\"", existing);
            Console.WriteLine(table + " count: " + await command.ExecuteScalarAsync());
        }
        foreach (var table in new[] { "payment_requests", "payment_transaction_attempts" })
        {
            await using var command = new NpgsqlCommand($"SELECT \"Status\",count(*) FROM \"{testSchema}\".\"{table}\" GROUP BY \"Status\" ORDER BY \"Status\"", existing);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) Console.WriteLine(table + " " + reader.GetString(0) + ": " + reader.GetInt64(1));
        }
        return 0;
    }
    if (mode == "pg-startup-check")
    {
        await using var transaction = await existing.BeginTransactionAsync();
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", existing, transaction)) await readOnly.ExecuteNonQueryAsync();
        var applied = new HashSet<string>();
        await using (var command = new NpgsqlCommand("SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\"", existing, transaction))
        await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) applied.Add(reader.GetString(0));
        var expected = Directory.GetFiles(Path.Combine(root,"src/Sentinel.Infrastructure/Persistence/Migrations"),"*.cs")
            .Select(Path.GetFileNameWithoutExtension).Where(x => x is not null && Regex.IsMatch(x,@"^\d{14}_[^.]+$")).ToArray();
        var pending = expected.Where(x => !applied.Contains(x!)).ToArray();
        foreach (var migration in pending) Console.WriteLine("Pending migration (not applied): " + migration);
        var tables = new HashSet<string>();
        await using (var command = new NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema='public'", existing, transaction))
        await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        var required = new[]{"audit_events","auth_challenges","payment_requests","payment_transaction_attempts","security_assessments","human_approvals","execution_permits","counterparty_blocklist","delegations","delegated_payments"};
        var missing = required.Where(x => !tables.Contains(x)).ToArray();
        foreach (var table in missing) Console.WriteLine("Missing application table: " + table);
        await transaction.RollbackAsync();
        if (pending.Length > 0 || missing.Length > 0) { Console.WriteLine("DATABASE_NOT_READY: specific migration approval required; no changes made."); return 2; }
        Console.WriteLine("DATABASE_READY: current migrations and application tables; inspection only, no migration applied."); return 0;
    }
    bool createDatabase;
    bool createSchema;
    await using (var command = new NpgsqlCommand("SELECT current_database(), current_user, current_setting('server_version'), rolcreatedb OR rolsuper, has_database_privilege(current_database(),'CREATE') FROM pg_roles WHERE rolname=current_user", existing))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        await reader.ReadAsync();
        createDatabase = reader.GetBoolean(3);
        createSchema = reader.GetBoolean(4);
        Console.WriteLine($"Connected: database={reader.GetString(0)}, role={reader.GetString(1)}, PostgreSQL={reader.GetString(2)}, can_create_database={createDatabase}, can_create_schema={createSchema}");
    }
    // Metadata only; never inspect application payment/audit rows.
    await using (var command = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename IN ('payment_requests','payment_transaction_attempts','audit_events','auth_challenges','__EFMigrationsHistory') ORDER BY tablename", existing))
    await using (var reader = await command.ExecuteReaderAsync())
        while (await reader.ReadAsync()) Console.WriteLine("Existing table (unchanged): " + reader.GetString(0));
    if (mode == "pg-inspect") {
        await using var exists = new NpgsqlCommand("SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL", existing);
        if ((bool)(await exists.ExecuteScalarAsync())!) {
            await using var history = new NpgsqlCommand("SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", existing);
            await using var reader = await history.ExecuteReaderAsync();
            while (await reader.ReadAsync()) Console.WriteLine("Applied migration (read-only): " + reader.GetString(0));
        }
        return 0;
    }

    var database = "sentinel_e2e_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
    if (!Regex.IsMatch(database, "^sentinel_e2e_[a-z0-9_]+$")) throw new InvalidOperationException("Unsafe test database name.");
    if (!createDatabase && !createSchema) throw new InvalidOperationException("Role cannot create an isolated database or schema.");
    var testSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
    if (createDatabase)
    {
        var adminSettings = new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Database = "postgres" };
        await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand("CREATE DATABASE \"" + database + "\"", admin);
        await create.ExecuteNonQueryAsync();
        testSettings.Database = database;
        Console.WriteLine("Created isolated test database: " + database);
    }
    else
    {
        await using var create = new NpgsqlCommand("CREATE SCHEMA \"" + database + "\"", existing);
        await create.ExecuteNonQueryAsync();
        testSettings.SearchPath = database; // Deliberately excludes public and $user.
        Console.WriteLine("Created isolated test schema: " + database + ". Existing application tables are outside its search path.");
    }
    if (mode == "pg-demo") {
        var demo = new ProcessStartInfo("dotnet") { UseShellExecute=false, CreateNoWindow=true, WorkingDirectory=root,
            RedirectStandardOutput=true, RedirectStandardError=true };
        foreach(var value in new[]{"run","--no-restore","--configuration","Verification","-p:UseAppHost=false","--project","tools/Sentinel.Demo"}) demo.ArgumentList.Add(value);
        if(args.Contains("--verify")) demo.ArgumentList.Add("--verify");
        demo.Environment["SENTINEL_E2E_DB"]=testSettings.ConnectionString;
        demo.Environment["SENTINEL_E2E_ROOT"]=root;
        using var demoProcess=Process.Start(demo)!;
        var demoOutput=demoProcess.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        var demoError=demoProcess.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        try { await demoProcess.WaitForExitAsync(); await Task.WhenAll(demoOutput,demoError); return demoProcess.ExitCode; }
        finally { if(!demoProcess.HasExited) demoProcess.Kill(true); }
    }
    var tests = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root,
        RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var value in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "tests/run-milestone1.ps1", "-ExistingDatabase" }) tests.ArgumentList.Add(value);
    var artifactsIndex=Array.IndexOf(args,"--artifacts-path");
    if(artifactsIndex>=0) {
        var artifacts=Path.GetFullPath(args.ElementAtOrDefault(artifactsIndex+1) ?? throw new ArgumentException("Artifacts path required."));
        var allowed=Path.GetFullPath(Path.Combine(root,".test-artifacts"))+Path.DirectorySeparatorChar;
        if(!artifacts.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Test artifacts must stay inside .test-artifacts.");
        tests.ArgumentList.Add("-ArtifactsPath");tests.ArgumentList.Add(artifacts);
    }
    tests.Environment["SENTINEL_E2E_DB"] = testSettings.ConnectionString;
    using var process = Process.Start(tests)!;
    var stdout = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
    var stderr = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
    await process.WaitForExitAsync();
    await Task.WhenAll(stdout, stderr);
    Console.WriteLine("Retained isolated test namespace: " + database);
    return process.ExitCode;
}
catch (PostgresException ex)
{
    Console.Error.WriteLine("PostgreSQL verification failed: SQLSTATE=" + ex.SqlState + ". No credentials printed.");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Verification could not complete: " + ex.GetType().Name + ". No credentials printed.");
    return 1;
}
