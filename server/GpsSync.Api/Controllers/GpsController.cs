using System.Security.Claims;
using GpsSync.Api.Data;
using GpsSync.Api.Dtos;
using GpsSync.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GpsSync.Api.Controllers;

[ApiController]
[Route("gps")]
[Authorize]
public class GpsController : ControllerBase
{
    private readonly AppDbContext _db;

    public GpsController(AppDbContext db) => _db = db;

    private string? CallerId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin => User.FindFirstValue("is_admin") == "true";

    // POST /gps — record a ping for the current user (engineer)
    [HttpPost]
    public async Task<IActionResult> Create(CreatePingRequest req)
    {
        var ping = new GpsPing
        {
            UserId = CallerId ?? string.Empty,
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            DisplayName = req.DisplayName,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.GpsPings.Add(ping);
        await _db.SaveChangesAsync();
        return Ok(ToDto(ping));
    }

    // GET /gps?userId=&limit=  — recent pings for a user (newest first).
    // Admin: any user. Engineer: forced to their own.
    [HttpGet]
    public async Task<IActionResult> List(string? userId, int limit = 100)
    {
        if (!IsAdmin) userId = CallerId;
        if (string.IsNullOrEmpty(userId)) return BadRequest(new { message = "userId is required." });
        limit = Math.Clamp(limit, 1, 500);

        var list = await _db.GpsPings
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync();
        return Ok(list.Select(ToDto));
    }

    // GET /gps/latest  (admin only) — the most recent ping per user (for the status board)
    [HttpGet("latest")]
    public async Task<IActionResult> Latest()
    {
        if (!IsAdmin) return Forbid();
        var recent = await _db.GpsPings
            .OrderByDescending(p => p.CreatedAt)
            .Take(500)
            .ToListAsync();
        var latest = recent.GroupBy(p => p.UserId).Select(g => g.First());
        return Ok(latest.Select(ToDto));
    }

    // DELETE /gps?userId=  (admin only) — remove all pings for a user
    [HttpDelete]
    public async Task<IActionResult> Delete(string userId)
    {
        if (!IsAdmin) return Forbid();
        if (string.IsNullOrEmpty(userId)) return BadRequest(new { message = "userId is required." });
        _db.GpsPings.RemoveRange(_db.GpsPings.Where(p => p.UserId == userId));
        var n = await _db.SaveChangesAsync();
        return Ok(new { deleted = n });
    }

    private static GpsPingDto ToDto(GpsPing p)
        => new(p.Id, p.UserId, p.Latitude, p.Longitude, p.DisplayName, p.CreatedAt);
}
