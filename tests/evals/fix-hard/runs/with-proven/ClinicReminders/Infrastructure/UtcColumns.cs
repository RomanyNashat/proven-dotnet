using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicReminders.Infrastructure;

/// <summary>
/// Timestamps are datetime2(3) holding UTC. Without the converter EF maps DateTimeOffset to datetimeoffset;
/// with it, a value sent with +03:00 is stored as the same instant in UTC.
/// </summary>
internal static class UtcColumns
{
    public static PropertyBuilder<DateTimeOffset> IsUtcDateTime2(this PropertyBuilder<DateTimeOffset> property) =>
        property.HasColumnType("datetime2(3)")
            .HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

    public static PropertyBuilder<DateTimeOffset?> IsUtcDateTime2(this PropertyBuilder<DateTimeOffset?> property) =>
        property.HasColumnType("datetime2(3)")
            .HasConversion(v => v!.Value.UtcDateTime, v => (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
}
