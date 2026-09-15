using GpsSync.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GpsSync.Api.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<DispatchJob> DispatchJobs => Set<DispatchJob>();
    public DbSet<GpsPing> GpsPings => Set<GpsPing>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Keep the old Supabase table names to ease later data migration.
        builder.Entity<Profile>(e =>
        {
            e.ToTable("profiles");
            e.HasKey(p => p.UserId);
        });

        builder.Entity<DispatchJob>(e =>
        {
            e.ToTable("dispatch_jobs");
            e.HasIndex(j => j.EngineerUserId);
            e.HasIndex(j => j.Status);
        });

        builder.Entity<GpsPing>(e =>
        {
            e.ToTable("gps_pings");
            e.HasIndex(g => g.UserId);
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasIndex(r => r.TokenHash);
            e.HasIndex(r => r.UserId);
        });

        builder.Entity<PasswordResetCode>(e =>
        {
            e.ToTable("password_reset_codes");
            e.HasIndex(c => c.Email);
        });
    }
}
