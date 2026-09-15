using Microsoft.AspNetCore.Identity;

namespace GpsSync.Api.Models;

/// <summary>
/// Application user. Inherits the standard ASP.NET Core Identity fields
/// (Id (string GUID), Email, PasswordHash, etc.). This replaces Supabase's auth.users.
/// </summary>
public class AppUser : IdentityUser
{
}
