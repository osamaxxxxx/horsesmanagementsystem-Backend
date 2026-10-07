using HorseVillage.Data;
using HorseVillage.Dtos;
using HorseVillage.Hubs;
using HorseVillage.Infrastructure;
using HorseVillage.Models;
using HorseVillage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HorseVillage.Controllers;

[Route("api/auth")]
public class AuthController(AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens, SyncNotifier notifier)
    : ApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Error(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "اسم المستخدم أو كلمة المرور غير صحيحة.");

        if (!user.IsActive)
            return Error(StatusCodes.Status403Forbidden, SyncNotifier.AccessRevoked, "تم سحب صلاحية الوصول لهذا الحساب.");

        var now = DateTimeOffset.UtcNow;
        var device = Truncate(request.DeviceInfo?.Trim() is { Length: > 0 } d ? d : Request.Headers.UserAgent.ToString(), 255);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceInfo = device,
            IpAddress = ip,
            CreatedAt = now,
            ExpiresAt = now.Add(tokens.Lifetime)
        };
        db.Sessions.Add(session);
        user.DeviceInfo = device;
        user.IpAddress = ip;
        await db.SaveChangesAsync(ct);

        return new LoginResponse(tokens.Create(session), session.ExpiresAt, ToCurrentUser(user));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var sessionId = User.GetSessionId();
        await db.Sessions.Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
        await notifier.RevokeSessionAsync(sessionId);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == CurrentUserId, ct);
        return ToCurrentUser(user);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == CurrentUserId, ct);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return Error(StatusCodes.Status400BadRequest, "INVALID_CREDENTIALS", "كلمة المرور الحالية غير صحيحة.");

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static CurrentUserDto ToCurrentUser(User user) => new(user.Id, user.Username, user.FullName, user.Role);

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
