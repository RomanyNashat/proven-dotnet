using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SkillSamples.MsSql;

public sealed class TenantRecord
{
    public int Id { get; private set; }

    public int TenantId { get; init; }

    public required string Note { get; init; }
}

public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
    /// <summary>Set per request, like the caller in efcore-patterns §1.</summary>
    public int TenantId { get; set; }

    public DbSet<TenantRecord> Records => Set<TenantRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var record = modelBuilder.Entity<TenantRecord>();
        record.ToTable("records");
        record.Property(r => r.Id).HasColumnName("id").UseIdentityColumn();
        record.Property(r => r.TenantId).HasColumnName("tenant_id");
        record.Property(r => r.Note).HasColumnName("note").HasMaxLength(200);
    }
}

/// <summary>
/// Tells SQL Server who the tenant is on every connection open; the security policy filters by it. The
/// tenant comes from the context being opened, not from a service injected here: interceptors in a
/// pooled context's options are built once, so an injected scoped service would be the first request's.
/// </summary>
public sealed class TenantSessionInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not TenantDbContext context)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'tenant_id', @value = @tenant, @read_only = 1;";
        command.Parameters.Add(new SqlParameter("@tenant", context.TenantId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
