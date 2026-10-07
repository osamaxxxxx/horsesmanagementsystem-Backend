using System.Text;
using HorseVillage.Infrastructure;
using HorseVillage.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HorseVillage.Services;

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "HorseVillage";
    public int ExpiryHours { get; set; } = 12;

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Key));
}

public class TokenService(JwtOptions options)
{
    public TimeSpan Lifetime => TimeSpan.FromHours(options.ExpiryHours);

    // The token only identifies the session; role and active state are read from the database on every request.
    public string Create(UserSession session) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = options.Issuer,
        Audience = options.Issuer,
        Expires = session.ExpiresAt.UtcDateTime,
        Claims = new Dictionary<string, object>
        {
            [ClaimsPrincipalExtensions.UserIdClaim] = session.UserId.ToString(),
            [ClaimsPrincipalExtensions.SessionIdClaim] = session.Id.ToString()
        },
        SigningCredentials = new SigningCredentials(options.SigningKey, SecurityAlgorithms.HmacSha256)
    });
}
