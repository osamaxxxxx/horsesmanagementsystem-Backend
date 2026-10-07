using System.Security.Claims;

namespace HorseVillage.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public const string UserIdClaim = "sub";
    public const string SessionIdClaim = "sid";
    public const string RoleClaim = "role";
    public const string NameClaim = "name";

    public static Guid GetUserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(UserIdClaim)!);

    public static Guid GetSessionId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(SessionIdClaim)!);
}
