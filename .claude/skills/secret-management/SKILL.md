---
name: secret-management
description: Secret management for .NET: no hardcoded secrets, IOptions config, Key Vault / env injection, rotation.
version: 1.0.0
---

# Secret Management Patterns

## Azure Key Vault (production recommended)

```csharp
// Configuration provider — secrets appear as IConfiguration values
builder.Configuration.AddAzureKeyVault(
    new Uri(builder.Configuration["KeyVault:Url"]!),
    new DefaultAzureCredential(),
    new AzureKeyVaultConfigurationOptions
    {
        ReloadInterval = TimeSpan.FromMinutes(5)  // auto-reload rotated secrets
    });

// Access secrets via IConfiguration — same as appsettings
var connectionString = builder.Configuration["ConnectionStrings:Default"];
var kafkaPassword = builder.Configuration["Kafka:SaslPassword"];

// Key Vault secret naming → configuration key mapping:
// "ConnectionStrings--Default" → "ConnectionStrings:Default"
// Double dashes become colons (: is invalid in KV names)
```

### Managed Identity (no passwords in config)
```csharp
// Azure: DefaultAzureCredential auto-detects environment
// Local dev: uses Azure CLI / VS credential
// AKS: uses Workload Identity
// App Service: uses System-assigned managed identity
var credential = new DefaultAzureCredential();

// Register for DI
builder.Services.AddSingleton<TokenCredential>(credential);
builder.Services.AddSingleton<SecretClient>(_ =>
    new SecretClient(new Uri(config["KeyVault:Url"]!), credential));
```

### Direct secret access (when needed beyond configuration)
```csharp
public sealed class KeyVaultSecretService(SecretClient client) : ISecretService
{
    public async Task<string> GetSecretAsync(string name, CancellationToken ct)
    {
        var response = await client.GetSecretAsync(name, cancellationToken: ct);
        return response.Value.Value;
    }

    public async Task SetSecretAsync(string name, string value, CancellationToken ct)
    {
        await client.SetSecretAsync(name, value, ct);
    }
}
```

## HashiCorp Vault (VaultSharp)

```csharp
// Registration
builder.Services.AddSingleton<IVaultClient>(_ =>
{
    var authMethod = new TokenAuthMethodInfo(config["Vault:Token"]);
    // Production: use AppRole, Kubernetes, or cloud auth methods
    // var authMethod = new KubernetesAuthMethodInfo(roleName, jwt);

    var settings = new VaultClientSettings(config["Vault:Url"], authMethod)
    {
        ContinueAsyncTasksOnCapturedContext = false
    };

    return new VaultClient(settings);
});

// Read secrets
public sealed class VaultSecretService(IVaultClient vaultClient) : ISecretService
{
    public async Task<string> GetSecretAsync(string path, CancellationToken ct)
    {
        var secret = await vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path, mountPoint: "secret");
        return secret.Data.Data["value"]?.ToString()
            ?? throw new KeyNotFoundException($"Secret not found: {path}");
    }

    public async Task<Dictionary<string, string>> GetSecretsAsync(
        string path, CancellationToken ct)
    {
        var secret = await vaultClient.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path, mountPoint: "secret");
        return secret.Data.Data.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.ToString() ?? "");
    }
}
```

## User Secrets (development only)

```bash
# Initialize user secrets for a project
dotnet user-secrets init

# Set secrets
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=orderdb;Username=dev;Password=devpass"
# SQL Server: "Server=localhost,1433;Database=orderdb;User Id=dev;Password=devpass;Encrypt=True;TrustServerCertificate=True"
dotnet user-secrets set "Kafka:SaslPassword" "local-kafka-password"
dotnet user-secrets set "Auth:ClientSecret" "dev-client-secret"

# List all secrets
dotnet user-secrets list

# Secrets stored at:
# Windows: %APPDATA%\Microsoft\UserSecrets\<user_secrets_id>\secrets.json
# macOS/Linux: ~/.microsoft/usersecrets/<user_secrets_id>/secrets.json
```

```csharp
// Auto-loaded in Development environment
// builder.Configuration already includes user secrets when
// Environment.IsDevelopment() is true
```

## Rotating Secret Pattern

```csharp
public sealed class RotatingSecretProvider(
    ISecretService secretService,
    IMemoryCache cache,
    ILogger<RotatingSecretProvider> logger) : IRotatingSecretProvider
{
    public async Task<string> GetConnectionStringAsync(CancellationToken ct)
    {
        return await cache.GetOrCreateAsync("db-connection-string", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var secret = await secretService.GetSecretAsync(
                "ConnectionStrings/Default", ct);

            logger.LogDebug("Refreshed database connection string from vault");
            return secret;
        }) ?? throw new InvalidOperationException("Failed to retrieve connection string");
    }
}
```

## Rotating database passwords

A rotated password must reach new connections without a restart, and connections already open keep
working until they close. The engines do this differently; both are tested in CI
(`tests/SkillSamples.Tests/Secrets`).

**PostgreSQL:** Npgsql asks for the password on a timer.

<!-- sample: tests/SkillSamples.Tests/Secrets/RotatingPostgres.cs -->
```csharp
/// <summary>A source of the current database password: a vault, a mounted secret file, a token service.</summary>
public interface IDatabasePassword
{
    ValueTask<string> GetAsync(CancellationToken ct);
}

public static class RotatingPostgres
{
    /// <summary>
    /// The connection string carries everything except the password. Npgsql asks for the password again
    /// every <paramref name="refresh"/>, and each new physical connection uses the latest one; connections
    /// already open stay as they are. Register the result as a singleton.
    /// </summary>
    public static NpgsqlDataSource Build(string connectionStringWithoutPassword, IDatabasePassword password, TimeSpan refresh)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionStringWithoutPassword);
        builder.UsePeriodicPasswordProvider(
            (_, ct) => password.GetAsync(ct),
            successRefreshInterval: refresh,
            failureRefreshInterval: TimeSpan.FromSeconds(10));
        return builder.Build();
    }
}
```

Tested: the role's password changes, the source returns the new one, and the next connection opens. The
connection string has no password; the data source is a singleton (`AddNpgsqlDataSource`, or register
the built one). The method is `UsePeriodicPasswordProvider`; there is no `ConfigurePeriodicPasswordProvider`.

**SQL Server:** SqlClient has no password callback, so the connection string is read per request.

<!-- sample: tests/SkillSamples.Tests/Secrets/RotatingSqlServer.cs -->
```csharp
/// <summary>The current connection string, kept fresh in the background from the vault.</summary>
public interface IDatabaseSecret
{
    string ConnectionString { get; }
}

public sealed class ReportsDbContext(DbContextOptions<ReportsDbContext> options) : DbContext(options);

public static class RotatingSqlServer
{
    /// <summary>
    /// SqlClient has no password callback. AddDbContext builds the options for every scope, so each request
    /// reads the current connection string; SqlClient pools by connection string, so the new password gets
    /// a new pool and the old pool's connections drain on their own. AddDbContextPool would build the
    /// options once and keep the first password.
    /// </summary>
    public static IServiceCollection AddReportsDb(this IServiceCollection services) =>
        services.AddDbContext<ReportsDbContext>((sp, options) =>
            options.UseSqlServer(sp.GetRequiredService<IDatabaseSecret>().ConnectionString));
}
```

Tested: after the login's password changes, the next request connects with the new one. The same
registration through `AddDbContextPool` keeps the first password and fails (login failed, 18456) once the
old pool is gone. With managed identity there is no password to rotate (`azure-deployment`).

The vault side of both is the cached provider above: `IDatabasePassword` / `IDatabaseSecret` read it, and
its cache period is how long a rotation takes to arrive.

## Environment-Specific Configuration Pattern

```csharp
// appsettings.json — defaults (no secrets)
{
    "ConnectionStrings": {
        "Default": ""  // empty — overridden per environment
    }
}

// Development → user-secrets
// Staging → Azure Key Vault (staging vault)
// Production → Azure Key Vault (production vault) + Managed Identity

// Program.cs
if (builder.Environment.IsProduction() || builder.Environment.IsStaging())
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(builder.Configuration["KeyVault:Url"]!),
        new DefaultAzureCredential());
}
// Development: user-secrets auto-loaded by default
```

## See also
- **`encryption-patterns`** — secrets protect keys; keys protect data. Rotation matters for both.
- **`openiddict-server`** — signing/encryption certificates come from Key Vault in production, with a
  rotation plan; dev certificates must never reach a deployed environment.
- **CI** — pipeline credentials (package feeds, analysis tokens) are masked, protected CI variables,
  generated at job time, never committed.

## Rules
- NEVER commit secrets to source control — use `.gitignore` for `secrets.json`, `.env`
- NEVER log secrets — not even partially (first 4 chars, etc.)
- NEVER hardcode secrets in code — not even "temporary" ones
- Use `dotnet user-secrets` for local development, vault for deployed environments
- Rotate secrets on a schedule (90 days maximum for production)
- Support zero-downtime rotation in code (dual-read pattern or cache refresh)
- Use managed identities (Azure) or service accounts (Vault) — avoid long-lived tokens
