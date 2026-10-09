using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SkillSamples.EfCore;

/// <summary>
/// The concurrency token as the client sees it: an opaque string. PostgreSQL's xmin is a uint, SQL
/// Server's rowversion is 8 bytes, and the client doesn't need to know which.
/// </summary>
public static class VersionToken
{
    public static string Read(DbContext db, object entity) =>
        db.Entry(entity).Property(VisitConfiguration.Version).CurrentValue switch
        {
            uint xmin => xmin.ToString(CultureInfo.InvariantCulture),
            byte[] rowVersion => Convert.ToBase64String(rowVersion),
            var other => throw new InvalidOperationException($"Unexpected concurrency token {other?.GetType().Name}"),
        };

    /// <summary>The save compares against the version the client loaded, not the one loaded just now.</summary>
    public static void Expect(DbContext db, object entity, string token)
    {
        var property = db.Entry(entity).Property(VisitConfiguration.Version);
        property.OriginalValue = property.Metadata.ClrType == typeof(uint)
            ? uint.Parse(token, CultureInfo.InvariantCulture)
            : Convert.FromBase64String(token);
    }
}
