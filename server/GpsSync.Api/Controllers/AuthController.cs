using System.Security.Claims;
using GpsSync.Api.Data;
using GpsSync.Api.Dtos;
using GpsSync.Api.Models;
using GpsSync.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GpsSync.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;

    public AuthController(UserManager<AppUser> users, AppDbContext db, ITokenService tokens, IEmailSender email)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
        _email = email;
    }

    // POST /auth/register  — parallels supabase.Auth.SignUp + profile insert
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var email = (req.Email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "Please enter a valid email address." });
        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 6)
            return BadRequest(new { message = "Password should be at least 6 characters." });

        if (await _users.FindByEmailAsync(email) is not null)
            return BadRequest(new { message = "User already registered." });

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
        {
            var msg = string.Join(" ", result.Errors.Select(e => e.Description));
            return BadRequest(new { message = string.IsNullOrWhiteSpace(msg) ? "Registration failed. Please try again." : msg });
        }

        _db.Profiles.Add(new Profile
        {
            UserId = user.Id,
            Email = email,
            IsAdmin = false,
            IsPunchedIn = false
        });
        await _db.SaveChangesAsync();

        return Ok(new { userId = user.Id, email });
    }

    // POST /auth/login  — parallels supabase.Auth.SignIn
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var email = (req.Email ?? string.Empty).Trim();
        var user = await _users.FindByEmailAsync(email);
        if (user is null || !await _users.CheckPasswordAsync(user, req.Password ?? string.Empty))
            return Unauthorized(new { message = "Invalid login credentials." });

        return Ok(await BuildAuthResponseAsync(user));
    }

    // POST /auth/refresh  — parallels supabase.Auth.RefreshSession
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest req)
    {
        var existing = await _tokens.ValidateRefreshTokenAsync(req.RefreshToken);
        if (existing is null)
            return Unauthorized(new { message = "Invalid or expired refresh token." });

        var user = await _users.FindByIdAsync(existing.UserId);
        if (user is null)
            return Unauthorized(new { message = "Invalid or expired refresh token." });

        await _tokens.RevokeAsync(existing); // rotate
        return Ok(await BuildAuthResponseAsync(user));
    }

    // POST /auth/logout  — parallels supabase.Auth.SignOut (best-effort refresh revoke)
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(LogoutRequest req)
    {
        if (!string.IsNullOrEmpty(req.RefreshToken))
            await _tokens.RevokeByRawAsync(req.RefreshToken);
        return NoContent();
    }

    // GET /auth/me  — current user
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var user = await _users.FindByIdAsync(userId);
        if (user is null) return Unauthorized();
        var profile = await _db.Profiles.FindAsync(userId);
        return Ok(new UserDto(user.Id, user.Email, profile?.DisplayName, profile?.IsAdmin ?? false));
    }

    // PATCH /auth/user  — parallels supabase.Auth.Update(display_name)
    [HttpPatch("user")]
    [Authorize]
    public async Task<IActionResult> UpdateUser(UpdateUserRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var profile = await _db.Profiles.FindAsync(userId);
        if (profile is null) return NotFound();

        if (req.DisplayName is not null)
            profile.DisplayName = req.DisplayName.Trim();
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST /auth/forgot-password — email a 6-digit reset code (parallels Auth.ResetPasswordForEmail)
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req)
    {
        var email = (req.Email ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var user = await _users.FindByEmailAsync(email);
            if (user is not null)
            {
                var code = Random.Shared.Next(100000, 1000000).ToString(); // 6 digits
                _db.PasswordResetCodes.Add(new PasswordResetCode
                {
                    Email = email,
                    CodeHash = HashCode(code),
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
                    Used = false
                });
                await _db.SaveChangesAsync();
                await _email.SendAsync(email, "Your password reset code",
                    $"Your password reset code is {code}. It expires in 15 minutes.");
            }
        }
        // Always 200 — never reveal whether an account exists for that email.
        return Ok(new { message = "If an account exists for that email, a reset code has been sent." });
    }

    // POST /auth/reset-password — verify the code and set a new password (parallels VerifyOTP + Update)
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
    {
        var email = (req.Email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "Email and code are required." });
        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { message = "Password should be at least 6 characters." });

        var codeHash = HashCode(req.Code.Trim());
        var entry = await _db.PasswordResetCodes
            .Where(c => c.Email == email && c.CodeHash == codeHash && !c.Used && c.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync();
        if (entry is null)
            return BadRequest(new { message = "Invalid or expired code. Please request a new one." });

        var user = await _users.FindByEmailAsync(email);
        if (user is null)
            return BadRequest(new { message = "Invalid or expired code. Please request a new one." });

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, req.NewPassword);
        if (!result.Succeeded)
        {
            var msg = string.Join(" ", result.Errors.Select(e => e.Description));
            return BadRequest(new { message = string.IsNullOrWhiteSpace(msg) ? "Failed to reset password." : msg });
        }

        entry.Used = true;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Password updated." });
    }

    private static string HashCode(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private async Task<AuthResponse> BuildAuthResponseAsync(AppUser user)
    {
        var profile = await _db.Profiles.FindAsync(user.Id);
        var isAdmin = profile?.IsAdmin ?? false;
        var access = _tokens.CreateAccessToken(user, isAdmin, out var expiresIn);
        var refresh = await _tokens.IssueRefreshTokenAsync(user.Id);
        return new AuthResponse(
            access,
            refresh,
            expiresIn,
            new UserDto(user.Id, user.Email, profile?.DisplayName, isAdmin));
    }
}
