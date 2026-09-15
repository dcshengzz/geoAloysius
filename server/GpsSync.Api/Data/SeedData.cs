using GpsSync.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace GpsSync.Api.Data;

public static class SeedData
{
    /// <summary>Creates a default admin account for local testing if it does not exist.</summary>
    public static async Task EnsureSeedAsync(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var db = services.GetRequiredService<AppDbContext>();

        const string adminEmail = "admin@local.test";
        const string adminPassword = "Admin!23";

        if (await users.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new AppUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var result = await users.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
            {
                db.Profiles.Add(new Profile
                {
                    UserId = admin.Id,
                    Email = adminEmail,
                    DisplayName = "Administrator",
                    IsAdmin = true,
                    IsPunchedIn = false
                });
                await db.SaveChangesAsync();
            }
        }
    }
}
