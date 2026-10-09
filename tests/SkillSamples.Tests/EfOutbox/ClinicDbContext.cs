using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SkillSamples.EfOutbox;

public sealed class ClinicDbContext(DbContextOptions<ClinicDbContext> options) : DbContext(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ClinicDailyCount> DailyCounts => Set<ClinicDailyCount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // A service has one engine and keeps one branch; both are here so CI checks both.
        var sqlServer = Database.IsSqlServer();
        modelBuilder.ApplyConfiguration(new AppointmentConfiguration(sqlServer));
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(sqlServer));
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration(sqlServer));
        modelBuilder.ApplyConfiguration(new ClinicDailyCountConfiguration());
    }
}

/// <summary>UTC timestamps under the column rules: timestamptz, or datetime2(3) with a converter.</summary>
internal static class UtcColumns
{
    public static void Utc(this PropertyBuilder<DateTimeOffset> property, bool sqlServer)
    {
        if (sqlServer)
        {
            property.HasColumnType("datetime2(3)")
                .HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        }
        else
        {
            property.HasColumnType("timestamptz");
        }
    }

    public static void Utc(this PropertyBuilder<DateTimeOffset?> property, bool sqlServer)
    {
        if (sqlServer)
        {
            property.HasColumnType("datetime2(3)")
                .HasConversion(v => v!.Value.UtcDateTime, v => (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        }
        else
        {
            property.HasColumnType("timestamptz");
        }
    }
}

public sealed class AppointmentConfiguration(bool sqlServer) : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");
        // int, known before SaveChanges. With one provider this is .UseHiLo("appointments_hilo"); the
        // samples project references both, so each call names its provider.
        var id = builder.Property(a => a.Id).HasColumnName("id");
        if (sqlServer)
        {
            SqlServerPropertyBuilderExtensions.UseHiLo(id, "appointments_hilo");
        }
        else
        {
            NpgsqlPropertyBuilderExtensions.UseHiLo(id, "appointments_hilo");
        }

        builder.Property(a => a.ClinicId).HasColumnName("clinic_id");
        builder.Property(a => a.StartsAt).HasColumnName("starts_at").Utc(sqlServer);
        builder.Property(a => a.Notes).HasColumnName("notes").HasMaxLength(200);
        builder.Property(a => a.IsCancelled).HasColumnName("is_cancelled");
        builder.Ignore(a => a.AggregateKey);
    }
}

public sealed class OutboxMessageConfiguration(bool sqlServer) : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox");
        var id = builder.Property(m => m.Id).HasColumnName("id");   // bigint: an event table grows
        if (sqlServer)
        {
            id.UseIdentityColumn();
        }
        else
        {
            id.UseIdentityAlwaysColumn();
        }

        builder.Property(m => m.Type).HasColumnName("type").HasMaxLength(200);
        builder.Property(m => m.AggregateKey).HasColumnName("aggregate_key").HasMaxLength(100);
        if (sqlServer)
        {
            builder.Property(m => m.Payload).HasColumnName("payload").HasMaxLength(4000);   // no nvarchar(max): small events, ids only
        }
        else
        {
            builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");   // jsonb: PostgreSQL only (column rules)
        }

        builder.Property(m => m.OccurredAt).HasColumnName("occurred_at").Utc(sqlServer);
        builder.Property(m => m.PublishedAt).HasColumnName("published_at").Utc(sqlServer);
        builder.Property(m => m.Attempts).HasColumnName("attempts");
        builder.Property(m => m.LastError).HasColumnName("last_error").HasMaxLength(200);
        builder.HasIndex(m => m.Id).HasDatabaseName("ix_outbox_pending").HasFilter("published_at IS NULL");
    }
}

public sealed class InboxMessageConfiguration(bool sqlServer) : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox");
        builder.HasKey(m => new { m.Consumer, m.MessageId });
        builder.Property(m => m.Consumer).HasColumnName("consumer").HasMaxLength(100);
        builder.Property(m => m.MessageId).HasColumnName("message_id").HasMaxLength(100);
        builder.Property(m => m.ProcessedAt).HasColumnName("processed_at").Utc(sqlServer);
    }
}

public sealed class ClinicDailyCountConfiguration : IEntityTypeConfiguration<ClinicDailyCount>
{
    public void Configure(EntityTypeBuilder<ClinicDailyCount> builder)
    {
        builder.ToTable("clinic_daily_counts");
        builder.HasKey(c => new { c.ClinicId, c.Day });
        builder.Property(c => c.ClinicId).HasColumnName("clinic_id");
        builder.Property(c => c.Day).HasColumnName("day");
        builder.Property(c => c.Booked).HasColumnName("booked");
    }
}
