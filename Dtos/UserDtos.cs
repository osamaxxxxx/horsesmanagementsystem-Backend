using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HorseVillage.Models;

namespace HorseVillage.Dtos;

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    [StringLength(255)]
    public string? DeviceInfo { get; set; }
}

public record CurrentUserDto(Guid Id, string Username, string FullName, UserRole Role);

public record LoginResponse(string Token, DateTimeOffset ExpiresAt, CurrentUserDto User);

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 10)]
    public string NewPassword { get; set; } = "";
}

public record UserDto(
    Guid Id,
    string Username,
    string FullName,
    UserRole Role,
    string? DeviceInfo,
    string? IpAddress,
    bool IsActive,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt,
    bool IsOnline);

public class CreateUserRequest
{
    [Required, StringLength(100, MinimumLength = 3)]
    public string Username { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 10)]
    public string Password { get; set; } = "";

    [Required, StringLength(255)]
    public string FullName { get; set; } = "";

    public UserRole Role { get; set; }
}

public class UpdateUserRequest
{
    [Required, StringLength(255)]
    public string FullName { get; set; } = "";

    public UserRole Role { get; set; }
}

public class ResetPasswordRequest
{
    [Required, StringLength(128, MinimumLength = 10)]
    public string NewPassword { get; set; } = "";
}

public record SessionDto(
    Guid Id,
    Guid UserId,
    string FullName,
    UserRole Role,
    string? DeviceInfo,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsOnline,
    bool IsCurrent);

public record AuditLogDto(
    Guid Id,
    Guid? PlotId,
    string ActionType,
    Guid? PerformedById,
    string? PerformedByName,
    JsonElement Details,
    DateTimeOffset CreatedAt);
