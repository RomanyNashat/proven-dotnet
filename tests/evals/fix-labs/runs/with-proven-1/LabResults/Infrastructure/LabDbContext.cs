using LabResults.Domain;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Infrastructure;

public sealed class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    public DbSet<LabResult> Results => Set<LabResult>();

    public DbSet<PendingSms> PendingSms => Set<PendingSms>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LabResult>(e =>
        {
            e.ToTable("lab_results");
            e.Property(r => r.Id).UseIdentityAlwaysColumn();
            e.Property(r => r.Test).HasMaxLength(LabResult.TestMaxLength);
            e.Property(r => r.Unit).HasMaxLength(LabResult.UnitMaxLength);
            e.Property(r => r.Value).HasColumnType("numeric(10,3)");
            e.Property(r => r.Notes).HasMaxLength(LabResult.NotesMaxLength);
            e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
            e.Property(r => r.RecordedBy).HasMaxLength(LabResult.RecordedByMaxLength);

            // Serves the keyset page: ORDER BY ReceivedAt DESC, Id DESC.
            e.HasIndex(r => new { r.ReceivedAt, r.Id });
        });

        modelBuilder.Entity<PendingSms>(e =>
        {
            e.ToTable("sms_outbox");
            e.Property(m => m.Id).UseIdentityAlwaysColumn();
            e.Property(m => m.Phone).HasMaxLength(Domain.PendingSms.PhoneMaxLength);
            e.Property(m => m.CreatedAt).HasColumnType("timestamptz");
            e.Property(m => m.SentAt).HasColumnType("timestamptz");
            e.Property(m => m.LastError).HasMaxLength(Domain.PendingSms.LastErrorMaxLength);
            e.Ignore(m => m.IsAbandoned);
            e.HasOne(m => m.Result).WithMany().HasForeignKey(m => m.ResultId).OnDelete(DeleteBehavior.Cascade);

            // The relay reads only unsent rows, oldest first.
            e.HasIndex(m => m.Id).HasFilter("\"SentAt\" IS NULL").HasDatabaseName("ix_sms_outbox_unsent");
        });
    }
}
