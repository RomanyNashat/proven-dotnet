using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SkillSamples.AuthServer;

public sealed class AppUser : IdentityUser<int>;
public sealed class AppRole : IdentityRole<int>
{
    public AppRole() { }
    public AppRole(string name) : base(name) { }
}

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AppUser, AppRole, int>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity leaves some strings unbounded (nvarchar(max)): bound every one it left open. OpenIddict's
        // own JSON columns (payloads, permissions, redirect URIs) stay unbounded: a recorded exception.
        foreach (var entity in builder.Model.GetEntityTypes().Where(e => e.ClrType.Namespace?.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal) == true
                                                                         || e.ClrType.Assembly == typeof(AppUser).Assembly))
        {
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is null))
            {
                property.SetMaxLength(property.Name switch
                {
                    nameof(IdentityUser.PasswordHash) => 200,
                    nameof(IdentityUser.PhoneNumber) => 32,
                    nameof(IdentityUserClaim<int>.ClaimValue) or nameof(IdentityUserToken<int>.Value) => 1000,
                    _ => 256
                });
            }
        }
    }
}
