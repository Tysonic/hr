using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Documents;
using TyHr.Application.Employees;
using TyHr.Application.Exits;
using TyHr.Application.Leave;
using TyHr.Application.Onboarding;
using TyHr.Application.Organization;
using TyHr.Application.Payroll;
using TyHr.Application.Performance;
using TyHr.Application.SelfService;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Performance;
using TyHr.Domain.Documents;
using TyHr.Domain.Exits;
using TyHr.Domain.Payroll;
using TyHr.Domain.Tenants;
using TyHr.Infrastructure.Files;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// One company must never see or change another's data. Company A gets a bit of everything, its names marked with
/// <see cref="Marker"/> and at least one row in every company-owned table; then a user of company B holding every company role calls every endpoint of the real API
/// pipeline (authentication, tenant middleware, policies, services, query filters) with every one of A's record ids.
/// Nothing of A's may come back, nothing of A's may change, and nothing may crash.
/// </summary>
public class TenantIsolationTests(PostgresFixture fx) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Marker = "Quixotic";
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Every_company_owned_table_is_limited_to_the_requests_company()
    {
        await using var scope = fx.NewScope(null);
        // Platform tables, shared by design: companies, the audit trail (filtered by company in AuditService) and number counters.
        string[] shared = ["Tenant", "AuditEntry", "DocumentSequence"];
        var types = scope.Db.Model.GetEntityTypes().Where(t => !t.IsOwned() && t.BaseType == null).ToList();
        Assert.Empty(types.Where(t => !typeof(ITenantOwned).IsAssignableFrom(t.ClrType) && !shared.Contains(t.ClrType.Name)).Select(t => t.ClrType.Name));
        Assert.Empty(types.Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType) && !t.GetDeclaredQueryFilters().Any()).Select(t => t.ClrType.Name));
    }

    [Fact]
    public void Every_endpoint_needs_a_sign_in_and_a_company_unless_it_says_why_not()
    {
        // Platform administration and the signed-in user's own context work without a company; the careers page is
        // public and sets the company from its URL; the audit trail filters by company (or none, for the platform) itself.
        string[] companyOptional = ["AuditController", "MeController", "CountriesController", "PlatformTenantsController", "CareersController"];
        var controllers = typeof(Program).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToList();
        Assert.Empty(controllers.Where(c => c.GetCustomAttribute<RequireTenantAttribute>() == null && !companyOptional.Contains(c.Name)).Select(c => c.Name));

        var anonymous = controllers.SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null || c.GetCustomAttribute<AllowAnonymousAttribute>() != null)
                .Select(m => c.Name)).Distinct();
        Assert.Equal(["CareersController"], anonymous); // everything else needs a sign-in with a TyHR role (the fallback policy), checked live below
    }

    [Fact]
    public async Task Another_companys_records_cant_be_read_or_changed_through_any_endpoint()
    {
        var a = await fx.CreateTenantAsync();
        var b = await fx.CreateTenantAsync();
        await FillAsync(a);
        await FillRemainingTablesAsync(a);

        await using var factory = Factory();
        var client = factory.CreateClient();
        var ids = await IdsAsync(a);
        var before = await FingerprintAsync(a);

        // The test can see a leak: A's own HR admin gets A's records back.
        Assert.Contains(Marker, await (await Send(client, HttpMethod.Get, "/api/employees", As(a))).Content.ReadAsStringAsync());
        Assert.Contains(Marker, await (await Send(client, HttpMethod.Get, $"/api/employees/{ids["Employee"][0]}", As(a))).Content.ReadAsStringAsync());

        var problems = new List<string>();
        var calls = 0;
        foreach (var (method, template, anonymous) in Endpoints(factory))
        {
            if (anonymous) continue; // the public careers page shows each company's own adverts by design
            using (var signedOut = await Send(client, method, Urls(template, ids, a).First(), null))
                if (signedOut.StatusCode != HttpStatusCode.Unauthorized) problems.Add($"{method} {template} answered {(int)signedOut.StatusCode} without a sign-in");
            foreach (var url in Urls(template, ids, a))
            {
                calls++;
                using var response = await Send(client, method, url, As(b));
                var body = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.InternalServerError) problems.Add($"500 from {method} {url}");
                else if (body.Contains(Marker, StringComparison.OrdinalIgnoreCase)) problems.Add($"{method} {url} returned company A's data ({(int)response.StatusCode})");
                else if (response.IsSuccessStatusCode && method != HttpMethod.Get && url.Split('/').Any(s => ids.Values.Any(v => v.Any(id => id.ToString() == s))))
                    problems.Add($"{method} {url} succeeded on company A's record ({(int)response.StatusCode})");
            }
        }
        Assert.True(calls > 2000, $"Only {calls} calls were made; is the endpoint list empty?");
        Assert.Empty(problems.Distinct().Take(40));
        Assert.Equal(before, await FingerprintAsync(a));
    }

    // Company A: a bit of every kind of record, with the marker in names and titles.

    private async Task FillAsync(string tenant)
    {
        await using var scope = fx.NewScope(tenant, user: "grace", roles: [Roles.HrAdmin], email: "grace@co.test");
        var access = new EmployeeAccess(scope.Db, scope.User);
        var email = new FakeEmail();
        var employees = new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock);
        var org = new OrgService(scope.Db, scope.TenantContext);

        var branch = await org.CreateBranchAsync(new($"{Marker} Branch", "QB", null), default);
        var dept = await org.CreateDepartmentAsync(new($"{Marker} Department", "QD", null, null), default);
        var grade = await org.CreateJobGradeAsync(new("Q1", $"{Marker} Grade", 1, null, null), default);
        await org.CreatePositionAsync(new($"{Marker} Analyst", dept.Id, grade.Id), default);

        var grace = (await employees.CreateAsync(EmployeeTests.NewEmployee("Grace", Marker, branchId: branch.Id, email: "grace@co.test"), default)).Id;
        var mark = (await employees.CreateAsync(EmployeeTests.NewEmployee("Mark", Marker, email: "mark@co.test"), default)).Id;
        var nora = (await employees.CreateAsync(EmployeeTests.NewEmployee("Nora", Marker, managerId: mark, email: "nora@co.test"), default)).Id;
        var ivan = (await employees.CreateAsync(EmployeeTests.NewEmployee("Ivan", Marker, email: "ivan@co.test"), default)).Id;
        await employees.AddNextOfKinAsync(nora, new($"{Marker} Kin", "Sister", "+256772000111", null, null), default);
        foreach (var id in new[] { grace, mark, nora, ivan })
            await employees.AddSalaryAsync(id, new AddSalaryRequest(new DateOnly(2025, 1, 6), 1_500_000, "Starting salary"), default);

        var store = new LocalFileStore(Options.Create(new StorageOptions { Path = _folder.Path }));
        var pdf = "%PDF-1.7\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();
        await new DocumentService(scope.Db, scope.TenantContext, store, scope.User, fx.Clock).UploadAsync(nora,
            new DocumentDetails($"{Marker} contract", DocumentCategory.Contract, null, null, null), "contract.pdf", pdf.Length, new MemoryStream(pdf), default);

        await LeaveDefaults.SeedAsync(scope.Db, CountryPacks.Uganda, 2026, default);
        var annual = await scope.Db.LeaveTypes.SingleAsync(t => t.Code == "ANNUAL");
        var documents = new DocumentService(scope.Db, scope.TenantContext, store, scope.User, fx.Clock);
        await new LeaveService(scope.Db, scope.TenantContext, access, scope.User, new LeaveNotifier(scope.Db, email, NullLogger<LeaveNotifier>.Instance), documents, fx.Clock)
            .CreateAsync(new CreateLeaveRequest(nora, annual.Id, new DateOnly(2026, 12, 7), new DateOnly(2026, 12, 8), false, $"{Marker} trip"), default);

        var payroll = new PayrollService(scope.Db, scope.TenantContext, scope.User, fx.Clock);
        await PayrollDefaults.SeedAsync(scope.Db, default);
        var run = await payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 10, PayrollRunKind.Regular, $"{Marker} October", null), default);
        await payroll.CalculateAsync(run.Id, default);

        await new AnnouncementService(scope.Db, scope.TenantContext, scope.User, fx.Clock).CreateAsync(new($"{Marker} news", "Body", false, null), default);
        await new OnboardingService(scope.Db, scope.TenantContext, scope.User, access, email, fx.Clock, NullLogger<OnboardingService>.Instance)
            .StartAsync(nora, new StartOnboardingRequest(null, ivan), default);
        var performance = new PerformanceService(scope.Db, scope.TenantContext, scope.User, access, email, fx.Clock, NullLogger<PerformanceService>.Instance);
        var cycle = await performance.CreateCycleAsync(new CycleDetails($"{Marker} 2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), true, null, null,
            null, [], PeerFeedback: true), default);
        await performance.LaunchAsync(cycle.Id, default);

        var exits = new ExitService(scope.Db, scope.TenantContext, scope.User, access, email, fx.Clock, NullLogger<ExitService>.Instance);
        var exit = (await exits.CreateAsync(new CreateExitRequest(ivan, ExitType.Resignation, $"{Marker} reasons", new DateOnly(2026, 11, 2),
            new DateOnly(2026, 12, 31), false, null), default)).Exit;
        await exits.InterviewAsync(exit.Id, new InterviewAnswers(LeavingReason.Pay, 3, 3, 3, 3, 3, true, true, $"{Marker} team", null, null), default);
    }

    /// <summary>
    /// A marked row in each company-owned table <see cref="FillAsync"/> left empty, built from the EF model, so every
    /// table (and any added later) is covered. The rows only need to be storable, not to make business sense.
    /// </summary>
    private async Task FillRemainingTablesAsync(string tenant)
    {
        await using var scope = fx.NewScope(tenant, user: "grace", roles: [Roles.HrAdmin], email: "grace@co.test");
        var db = scope.Db;
        var firstIds = new Dictionary<Type, object?>();
        async Task<object?> FirstAsync(IEntityType type)
        {
            if (firstIds.TryGetValue(type.ClrType, out var known)) return known;
            var set = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(type.ClrType).Invoke(db, null)!;
            return firstIds[type.ClrType] = await set.AsNoTracking().FirstOrDefaultAsync();
        }
        async Task<object> EnsureAsync(IEntityType type)
        {
            if (await FirstAsync(type) is { } existing) return existing;
            var row = Activator.CreateInstance(type.ClrType, nonPublic: true)!;
            var entry = db.Entry(row);
            var links = new Dictionary<IProperty, object?>();
            foreach (var fk in type.GetForeignKeys().Where(f => f.IsRequired && !f.PrincipalEntityType.IsOwned() && f.PrincipalEntityType != type))
            {
                var principal = await EnsureAsync(fk.PrincipalEntityType);
                for (var i = 0; i < fk.Properties.Count; i++)
                    links[fk.Properties[i]] = fk.PrincipalKey.Properties[i].PropertyInfo!.GetValue(principal);
            }
            foreach (var property in type.GetProperties().Where(p => p.ValueGenerated != ValueGenerated.OnAddOrUpdate))
            {
                if (links.TryGetValue(property, out var link)) entry.Property(property).CurrentValue = link;
                else if (property.ClrType == typeof(Guid) && property.IsPrimaryKey()) entry.Property(property).CurrentValue = Guid.NewGuid();
                else if (property.IsForeignKey()) continue;
                else if (property.ClrType == typeof(string) && property.Name != nameof(ITenantOwned.Tenant))
                {
                    var length = property.GetMaxLength() ?? 100;
                    entry.Property(property).CurrentValue = Marker.Length <= length ? Marker : Marker[..length];
                }
                else if (property.ClrType.IsEnum) entry.Property(property).CurrentValue = Enum.GetValues(property.ClrType).GetValue(0);
                else if (property.ClrType == typeof(DateOnly)) entry.Property(property).CurrentValue = new DateOnly(2026, 10, 1);
                else if (property.ClrType == typeof(DateTimeOffset)) entry.Property(property).CurrentValue = fx.Clock.GetUtcNow();
            }
            db.Add(row);
            try { await db.SaveChangesAsync(); }
            catch (Exception e) { throw new InvalidOperationException($"Couldn't make a sample {type.ClrType.Name}; add one to FillAsync. {e.InnerException?.Message ?? e.Message}", e); }
            return firstIds[type.ClrType] = row;
        }
        foreach (var type in db.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType) && !t.IsOwned()).ToList())
            await EnsureAsync(type);
    }

    /// <summary>Up to five ids of each kind of company A record (every employee's), by entity name.</summary>
    private async Task<Dictionary<string, List<Guid>>> IdsAsync(string tenant)
    {
        await using var scope = fx.NewScope(tenant);
        var result = new Dictionary<string, List<Guid>>();
        foreach (var type in scope.Db.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType) && !t.IsOwned()))
        {
            if (type.ClrType.GetProperty("Id")?.PropertyType != typeof(Guid)) continue;
            var set = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(type.ClrType).Invoke(scope.Db, null)!;
            var rows = await set.AsNoTracking().Take(type.ClrType.Name == "Employee" ? 50 : 5).ToListAsync();
            if (rows.Count > 0) result[type.ClrType.Name] = rows.Select(r => (Guid)type.ClrType.GetProperty("Id")!.GetValue(r)!).ToList();
        }
        // A company-owned table with no row isn't being tested. Tables keyed by something other than a Guid Id have no id to try.
        var missing = scope.Db.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType) && !t.IsOwned()
            && t.ClrType.GetProperty("Id")?.PropertyType == typeof(Guid) && !result.ContainsKey(t.ClrType.Name)).Select(t => t.ClrType.Name);
        Assert.Empty(missing);
        return result;
    }

    /// <summary>What would change if anything of company A's changed: its audit trail (every change is audited) and its unaudited payslips.</summary>
    private async Task<(int, int, string)> FingerprintAsync(string tenant)
    {
        await using var scope = fx.NewScope(tenant);
        var audit = await scope.Db.AuditEntries.AsNoTracking().CountAsync(e => e.Tenant == tenant);
        var slips = await scope.Db.Payslips.AsNoTracking().OrderBy(p => p.Id).Select(p => p.Net).ToListAsync();
        var runs = await scope.Db.PayrollRuns.AsNoTracking().OrderBy(r => r.Id).Select(r => r.Status).ToListAsync();
        return (audit, slips.Count, string.Join(",", slips) + "|" + string.Join(",", runs));
    }

    // The API, signed in by a header instead of Keycloak.

    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Testing");
        b.UseSetting("ConnectionStrings:Default", fx.ConnectionString);
        b.UseSetting("Auth:Authority", "http://auth.invalid/realms/test");
        b.UseSetting("Database:MigrateOnStartup", "false");
        b.UseSetting("Secrets:Key", PostgresFixture.SecretsKey);
        b.UseSetting("Storage:Path", _folder.Path);
        b.ConfigureLogging(l => l.ClearProviders());
        b.ConfigureTestServices(s =>
        {
            foreach (var job in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ReminderJob)).ToList()) s.Remove(job);
            s.AddAuthentication(o => { o.DefaultScheme = TestAuth.Name; o.DefaultAuthenticateScheme = TestAuth.Name; o.DefaultChallengeScheme = TestAuth.Name; })
                .AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Name, null);
        });
    });

    private static string As(string tenant) => $"hr-{tenant}|{tenant}|{string.Join(",", Roles.Company)}|hr@{tenant}.test";

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, string? user)
    {
        var request = new HttpRequestMessage(method, url);
        if (user != null) request.Headers.Add(TestAuth.Header, user);
        if (method != HttpMethod.Get && method != HttpMethod.Delete) request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return client.SendAsync(request);
    }

    private static IEnumerable<(HttpMethod Method, string Template, bool Anonymous)> Endpoints(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (new HttpMethod(m), "/" + e.RoutePattern.RawText!.TrimStart('/'),
                e.Metadata.GetMetadata<IAllowAnonymous>() != null)))
            .Distinct();

    /// <summary>The template with each id parameter set to each of company A's ids, and other parameters to plausible values.</summary>
    private static IEnumerable<string> Urls(string template, Dictionary<string, List<Guid>> ids, string tenantA)
    {
        var parameters = System.Text.RegularExpressions.Regex.Matches(template, @"\{([^}:]+)(:[^}]+)?\}").ToList();
        var guids = parameters.Where(p => p.Groups[2].Value.Contains("guid")).ToList();
        string Fill(string t, Guid? first, Guid? second)
        {
            var g = 0;
            foreach (var p in parameters)
            {
                var value = p.Groups[2].Value.Contains("guid") ? (g++ == 0 ? first : second)?.ToString()
                    : p.Groups[1].Value switch { "year" => "2026", "month" => "10", "slug" => tenantA, _ when p.Groups[2].Value.Contains("int") => "1", _ => "x" };
                t = t.Replace(p.Value, value);
            }
            return t;
        }
        var all = ids.Values.SelectMany(v => v).Distinct().ToList();
        return guids.Count switch
        {
            0 => [Fill(template, null, null)],
            1 => all.Select(id => Fill(template, id, null)),
            // Two ids (e.g. an employee's document): each employee with every id.
            _ => ids["Employee"].SelectMany(e => all.Select(id => Fill(template, e, id))),
        };
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";
        public const string Header = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers[Header].FirstOrDefault() is not { } value) return Task.FromResult(AuthenticateResult.NoResult());
            var parts = value.Split('|');
            var claims = new List<Claim> { new("preferred_username", parts[0]), new(TenantSlug.ClaimType, parts[1]), new("email", parts[3]) };
            claims.AddRange(parts[2].Split(',').Select(r => new Claim(ClaimTypes.Role, r)));
            var identity = new ClaimsIdentity(claims, Name, "preferred_username", ClaimTypes.Role);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
