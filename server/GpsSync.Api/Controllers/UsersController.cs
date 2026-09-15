using System.Security.Claims;
using GpsSync.Api.Data;
using GpsSync.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GpsSync.Api.Controllers;

[ApiController]
[Route("users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _users;

    public UsersController(AppDbContext db, UserManager<AppUser> users)
    {
        _db = db;
        _users = users;
    }

    private bool IsAdmin => User.FindFirstValue("is_admin") == "true";

    // DELETE /users/{userId}  (admin only) — remove the engineer entirely:
    // their pings, jobs, refresh tokens, profile, and the identity account.
    [HttpDelete("{userId}")]
    public async Task<IActionResult> Delete(string userId)
    {
        if (!IsAdmin) return Forbid();

        _db.GpsPings.RemoveRange(_db.GpsPings.Where(p => p.UserId == userId));
        _db.DispatchJobs.RemoveRange(_db.DispatchJobs.Where(j => j.EngineerUserId == userId));
        _db.RefreshTokens.RemoveRange(_db.RefreshTokens.Where(r => r.UserId == userId));
        var profile = await _db.Profiles.FindAsync(userId);
        if (profile is not null) _db.Profiles.Remove(profile);
        await _db.SaveChangesAsync();

        var user = await _users.FindByIdAsync(userId);
        if (user is not null) await _users.DeleteAsync(user);

        return NoContent();
    }
}
