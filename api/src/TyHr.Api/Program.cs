using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Infrastructure;
using TyHr.Infrastructure.Persistence;
using TyHr.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<ReminderOptions>(builder.Configuration.GetSection("Reminders"));
builder.Services.AddHostedService<ReminderJob>();
// A relative Storage:Path is relative to the app's folder, however the API was started.
builder.Services.PostConfigure<TyHr.Infrastructure.Files.StorageOptions>(o =>
    o.Path = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, o.Path)));
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(TyHr.Api.Controllers.CareersController.AddLimits);

// The API is only reachable through Caddy (it publishes no port), and Caddy sets X-Forwarded-For to the real client
// address, replacing whatever the client sent. Trust that one hop, so per-client limits see the client, not Caddy.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// ASP.NET's data-protection keys (used if anything ever protects data: cookies, antiforgery tokens). In a container
// they'd be lost on every redeploy, so production keeps them on a volume (DataProtection:KeysPath).
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    builder.Services.AddDataProtection().SetApplicationName("TyHr").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// Sign-in is through the shared Tysoft Keycloak realm (Tycount's), so one sign-in covers Tycount and TyHR; the API
// accepts access tokens issued for tyhr-api. See deploy/keycloak/setup.sh.
var auth = builder.Configuration.GetSection("Auth");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = auth["Authority"] ?? throw new InvalidOperationException("Auth:Authority is not configured.");
        o.Audience = auth["Audience"] ?? "tyhr-api";
        o.RequireHttpsMetadata = auth.GetValue("RequireHttpsMetadata", true);
        o.MapInboundClaims = false;
        o.TokenValidationParameters.NameClaimType = "preferred_username";
        o.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
    });
builder.Services.Configure<RoleOptions>(auth);
builder.Services.AddTransient<IClaimsTransformation, KeycloakRolesTransformation>();
builder.Services.AddAuthorization(PermissionPolicies.AddPolicies);

builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5174"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    // The database may still be starting (a server reboot): retry for about a minute. If it never answers, exit
    // with an error code so the container's restart policy tries again; an unhandled exception here was seen to leave
    // the process hung instead, so nothing restarted it.
    var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            if (app.Environment.IsDevelopment())
                await DevelopmentSeed.RunAsync(db, scope.ServiceProvider.GetRequiredService<ITenantContext>(), TimeProvider.System);
            break;
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        {
            if (attempt >= 30)
            {
                startupLog.LogCritical(ex, "The database is unreachable; giving up after {Attempts} attempts.", attempt);
                Environment.Exit(1);
            }
            startupLog.LogWarning("The database is not reachable yet ({Error}); retrying ({Attempt}/30).", ex.Message, attempt);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

// Business-rule violations become 400 responses with a readable message.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var problem = error switch
    {
        DomainException => new ProblemDetails { Status = 400, Title = "Business rule violation", Detail = error.Message },
        NotFoundException => new ProblemDetails { Status = 404, Title = "Not found", Detail = error.Message },
        _ => new ProblemDetails { Status = 500, Title = "An unexpected error occurred" }
    };
    context.Response.StatusCode = problem.Status!.Value;
    await context.Response.WriteAsJsonAsync(problem);
}));

if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseForwardedHeaders();
app.UseCors("frontend");

// 401 and 403 carry a readable message like other errors, so the web app can show it.
app.UseStatusCodePages(context => context.HttpContext.Response.StatusCode switch
{
    401 => Results.Problem(statusCode: 401, title: "Not signed in", detail: "Sign in to continue.").ExecuteAsync(context.HttpContext),
    403 => Results.Problem(statusCode: 403, title: "Not allowed", detail: "You don't have permission to do this.").ExecuteAsync(context.HttpContext),
    429 => Results.Problem(statusCode: 429, title: "Too many requests", detail: "Too many attempts. Please wait a few minutes and try again.")
        .ExecuteAsync(context.HttpContext),
    _ => Task.CompletedTask,
});

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

// Health: the process is up and the database answers. /api/health is the same check through Caddy (which only
// forwards /api/* to the API), for uptime monitoring; /health is for the container's own healthcheck.
static async Task<IResult> Health(AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ok" })
        : Results.Json(new { status = "database unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
app.MapGet("/health", Health).AllowAnonymous();
app.MapGet("/api/health", Health).AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program;
