using System.Security.Claims;
using GpsSync.Api.Data;
using GpsSync.Api.Dtos;
using GpsSync.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GpsSync.Api.Controllers;

[ApiController]
[Route("jobs")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly AppDbContext _db;

    public JobsController(AppDbContext db) => _db = db;

    private string? CallerId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin => User.FindFirstValue("is_admin") == "true";

    // GET /jobs?engineerUserId=&adminUserId=&status=
    // Admin: sees all (optionally filtered). Engineer: always scoped to their own jobs.
    [HttpGet]
    public async Task<IActionResult> List(string? engineerUserId, string? adminUserId, string? status)
    {
        var q = _db.DispatchJobs.AsQueryable();

        if (!IsAdmin)
            engineerUserId = CallerId; // force engineers to only see their own jobs

        if (!string.IsNullOrEmpty(engineerUserId)) q = q.Where(j => j.EngineerUserId == engineerUserId);
        if (!string.IsNullOrEmpty(adminUserId)) q = q.Where(j => j.AdminUserId == adminUserId);
        if (!string.IsNullOrEmpty(status)) q = q.Where(j => j.Status == status);

        var jobs = await q.OrderByDescending(j => j.CreatedAt).ToListAsync();
        return Ok(jobs.Select(ToDto));
    }

    // POST /jobs  (admin only) — dispatch a job to an engineer
    [HttpPost]
    public async Task<IActionResult> Create(CreateJobRequest req)
    {
        if (!IsAdmin) return Forbid();
        if (string.IsNullOrWhiteSpace(req.EngineerUserId) || string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "engineerUserId and title are required." });

        var job = new DispatchJob
        {
            EngineerUserId = req.EngineerUserId,
            AdminUserId = CallerId ?? string.Empty,
            Title = req.Title.Trim(),
            Description = req.Description?.Trim() ?? string.Empty,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        _db.DispatchJobs.Add(job);
        await _db.SaveChangesAsync();
        return Ok(ToDto(job));
    }

    // PATCH /jobs/{id} — change status. Admin: any job. Engineer: only their own.
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id, UpdateJobRequest req)
    {
        var job = await _db.DispatchJobs.FindAsync(id);
        if (job is null) return NotFound();
        if (!IsAdmin && job.EngineerUserId != CallerId) return Forbid();

        job.Status = req.Status;
        if (req.Status == "Completed")
            job.CompletedAt ??= DateTime.UtcNow;
        else
            job.CompletedAt = null;

        await _db.SaveChangesAsync();
        return Ok(ToDto(job));
    }

    // DELETE /jobs/completed  (admin only) — clear the completed jobs from the board
    [HttpDelete("completed")]
    public async Task<IActionResult> DeleteCompleted()
    {
        if (!IsAdmin) return Forbid();
        var completed = _db.DispatchJobs.Where(j => j.Status == "Completed");
        _db.DispatchJobs.RemoveRange(completed);
        var n = await _db.SaveChangesAsync();
        return Ok(new { deleted = n });
    }

    private static JobDto ToDto(DispatchJob j)
        => new(j.Id, j.EngineerUserId, j.AdminUserId, j.Title, j.Description, j.Status, j.CreatedAt, j.CompletedAt);
}
