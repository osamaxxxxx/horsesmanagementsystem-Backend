using System.Security.Claims;
using System.Threading.RateLimiting;
using HorseVillage.Data;
using HorseVillage.Hubs;
using HorseVillage.Infrastructure;
using HorseVillage.Models;
using HorseVillage.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be configured with at least 32 characters.");

// Add services to the container.

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<SyncNotifier>();

builder.Services.AddControllers().AddJsonOptions(o => JsonDefaults.Apply(o.JsonSerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(o => JsonDefaults.Apply(o.PayloadSerializerOptions));
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Horse Village API", Version = "v1" });
    // Paste the token from /api/auth/login into the Authorize dialog.
    var bearer = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityDefinition("Bearer", bearer);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });
});

const string denialCodeKey = "AuthDenialCode";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Issuer,
            IssuerSigningKey = jwt.SigningKey,
            NameClaimType = ClaimsPrincipalExtensions.NameClaim,
            RoleClaimType = ClaimsPrincipalExtensions.RoleClaim,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            // Browsers cannot set headers on WebSocket requests, so the hub takes the token from the query string.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = token;
                return Task.CompletedTask;
            },
            // Revocation check on every request: the session and the account must both still be live.
            OnTokenValidated = async context =>
            {
                var http = context.HttpContext;
                var db = http.RequestServices.GetRequiredService<AppDbContext>();
                var principal = context.Principal!;
                Guid.TryParse(principal.FindFirstValue(ClaimsPrincipalExtensions.SessionIdClaim), out var sessionId);
                // Not tied to RequestAborted: a browser cancelling mid-check (e.g. a page reload) would otherwise
                // surface here as a TaskCanceledException that the JWT handler logs as an authentication failure.
                var session = await db.Sessions.AsNoTracking()
                    .Where(s => s.Id == sessionId)
                    .Select(s => new { s.RevokedAt, s.User.IsActive, s.User.Role, s.User.FullName })
                    .FirstOrDefaultAsync();

                if (session is null || session.RevokedAt is not null || !session.IsActive)
                {
                    http.Items[denialCodeKey] = session is { IsActive: false } ? SyncNotifier.AccessRevoked : SyncNotifier.SessionEnded;
                    context.Fail("Session is no longer valid.");
                    return;
                }

                // Role comes from the database so a role change applies immediately.
                var identity = (ClaimsIdentity)principal.Identity!;
                identity.AddClaim(new Claim(ClaimsPrincipalExtensions.RoleClaim, session.Role.ToString()));
                identity.AddClaim(new Claim(ClaimsPrincipalExtensions.NameClaim, session.FullName));
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (context.Response.HasStarted) return;

                var problem = new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized" };
                problem.Extensions["code"] = context.HttpContext.Items[denialCodeKey] as string ?? "UNAUTHENTICATED";
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// Open to any origin. Requests are authorized by the bearer token, not by cookies, so the origin is not what protects the API.
// (AllowAnyOrigin cannot be combined with credentials, which the SignalR browser client sends, hence the predicate.)
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(_ => true)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders("Content-Disposition")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    if (app.Environment.IsDevelopment())
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await DbSeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SyncHub>("/hubs/sync", options => options.CloseOnAuthenticationExpiration = true);

app.Run();
