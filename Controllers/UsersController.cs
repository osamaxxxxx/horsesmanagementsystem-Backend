using HorseVillage.Data;
using HorseVillage.Dtos;
using HorseVillage.Hubs;
using HorseVillage.Infrastructure;
using HorseVillage.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HorseVillage.Controllers;

// Security center: user management, active sessions and instant revocation. Reporter only.
[Route("api/users")]
[Authorize(Roles = Roles.SuperAdmin)]
public class UsersController(AppDbContext db, IPasswordHasher<User> hasher, PresenceTracker presence, SyncNotifier notifier)
    : ApiControllerBase
{
    [HttpGet]
    public async Task<List<UserDto>> List(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(ct);
        return users.Select(ToDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            return Error(StatusCodes.Status409Conflict, "DUPLICATE_USERNAME", $"اسم المستخدم {username} مستخدم بالفعل.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            FullName = request.FullName.Trim(),
            Role = request.Role,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        if (id == CurrentUserId && request.Role != user.Role)
            return Error(StatusCodes.Status409Conflict, "CANNOT_CHANGE_OWN_ROLE", "لا يمكنك تغيير دورك بنفسك.");

        user.FullName = request.FullName.Trim();
        user.Role = request.Role;
        await db.SaveChangesAsync(ct);
        await notifier.ProfileChangedAsync(id);
        return ToDto(user);
    }

    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Kill switch: blocks the account, ends every session and locks the user's open screens.
    [HttpPost("{id:guid}/revoke")]
    public async Task<ActionResult<UserDto>> Revoke(Guid id, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Sessions.Where(s => s.RevokedAt == null)).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        if (user.Role == UserRole.SUPER_ADMIN_REPORTER)
            return Error(StatusCodes.Status409Conflict, "CANNOT_REVOKE_SUPER_ADMIN", "لا يمكن سحب صلاحية مقرر اللجنة.");

        if (user.IsActive)
        {
            var now = DateTimeOffset.UtcNow;
            user.IsActive = false;
            user.RevokedAt = now;
            foreach (var session in user.Sessions)
                session.RevokedAt = now;
            db.AuditLogs.Add(AuditLog.Create(AuditActions.Revoke, CurrentUserId, new { userId = user.Id, user.Username, user.FullName }));
            await db.SaveChangesAsync(ct);
        }

        await notifier.RevokeUserAsync(id);
        return ToDto(user);
    }

    // Re-enables the account. Old sessions stay dead; the user has to sign in again.
    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<UserDto>> Restore(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        if (!user.IsActive)
        {
            user.IsActive = true;
            user.RevokedAt = null;
            db.AuditLogs.Add(AuditLog.Create(AuditActions.Restore, CurrentUserId, new { userId = user.Id, user.Username, user.FullName }));
            await db.SaveChangesAsync(ct);
        }

        return ToDto(user);
    }

    [HttpGet("sessions")]
    public async Task<List<SessionDto>> Sessions(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var currentSessionId = User.GetSessionId();
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.RevokedAt == null && s.ExpiresAt > now && s.User.IsActive)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.UserId, s.User.FullName, s.User.Role, s.DeviceInfo, s.IpAddress, s.CreatedAt, s.ExpiresAt })
            .ToListAsync(ct);

        return sessions.Select(s => new SessionDto(s.Id, s.UserId, s.FullName, s.Role, s.DeviceInfo, s.IpAddress, s.CreatedAt,
            s.ExpiresAt, presence.IsSessionOnline(s.Id), s.Id == currentSessionId)).ToList();
    }

    // Ends a single device's session without blocking the account.
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> EndSession(Guid sessionId, CancellationToken ct)
    {
        var updated = await db.Sessions.Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
        if (updated == 0) return NotFound();

        await notifier.RevokeSessionAsync(sessionId);
        return NoContent();
    }

    private UserDto ToDto(User user) => new(user.Id, user.Username, user.FullName, user.Role, user.DeviceInfo, user.IpAddress,
        user.IsActive, user.RevokedAt, user.CreatedAt, presence.IsUserOnline(user.Id));
}
