using System.Security.Claims;
using GpsSync.Api.Data;
using GpsSync.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GpsSync.Api.Controllers;

[ApiController]
[Route("profiles")]
[Authorize]
public class ProfilesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProfilesController(AppDbContext db) => _db = db;

    // GET /profiles  (admin only) — list all profiles (name lookup, engineer status, punch monitoring)
    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (User.FindFirstValue("is_admin") != "true") return Forbid();
        var all = await _db.Profiles.ToListAsync();
        return Ok(all.Select(p => new ProfileDto(p.UserId, p.IsAdmin, p.DisplayName, p.Email, p.IsPunchedIn, p.ActiveDeviceToken)));
    }

    // GET /profiles/{userId}  — parallels supabase.From<ProfileRecord>().Filter(user_id).Single()
    [HttpGet("{userId}")]
    public async Task<IActionResult> Get(string userId)
    {
        if (!CanAccess(userId)) return Forbid();
        var p = await _db.Profiles.FindAsync(userId);
        if (p is null) return NotFound();
        return Ok(new ProfileDto(p.UserId, p.IsAdmin, p.DisplayName, p.Email, p.IsPunchedIn, p.ActiveDeviceToken));
    }

    // PATCH /profiles/{userId}  — partial update (active_device_token, display_name, is_punched_in)
    [HttpPatch("{userId}")]
    public async Task<IActionResult> Update(string userId, UpdateProfileRequest req)
    {
        if (!CanAccess(userId)) return Forbid();
        var p = await _db.Profiles.FindAsync(userId);
        if (p is null) return NotFound();

        if (req.DisplayName is not null) p.DisplayName = req.DisplayName.Trim();
        if (req.IsPunchedIn.HasValue) p.IsPunchedIn = req.IsPunchedIn.Value;
        if (req.ClearActiveDeviceToken == true) p.ActiveDeviceToken = null;
        else if (req.ActiveDeviceToken is not null) p.ActiveDeviceToken = req.ActiveDeviceToken;

        await _db.SaveChangesAsync();
        return Ok(new ProfileDto(p.UserId, p.IsAdmin, p.DisplayName, p.Email, p.IsPunchedIn, p.ActiveDeviceToken));
    }

    /// <summary>A user may act on their own profile; admins may act on any profile.</summary>
    private bool CanAccess(string targetUserId)
    {
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.FindFirstValue("is_admin") == "true";
        return isAdmin || string.Equals(callerId, targetUserId, StringComparison.Ordinal);
    }
}
