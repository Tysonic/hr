using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TyHr.Application.Common;
using TyHr.Application.Documents;
using TyHr.Application.Employees;
using TyHr.Application.Reminders;
using TyHr.Domain;
using TyHr.Domain.Documents;
using TyHr.Domain.Employees;
using TyHr.Domain.Reminders;
using TyHr.Infrastructure.Files;

namespace TyHr.Tests;

/// <summary>Records emails instead of sending them; "unconfigured" behaves like an empty Mail:Host.</summary>
public class FakeEmail : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];
    public bool Configured { get; set; } = true;
    /// <summary>Addresses the server refuses.</summary>
    public HashSet<string> Refused { get; } = [];
    /// <summary>Runs before each send, e.g. to make the server go away.</summary>
    public Action? BeforeSend { get; set; }

    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!Configured) return Task.FromResult(false);
        BeforeSend?.Invoke();
        if (message.To.Any(Refused.Contains)) throw new InvalidOperationException("550 no such mailbox");
        Sent.Add(message);
        return Task.FromResult(true);
    }
}

public sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("tyhr-files-").FullName;
    public void Dispose() => Directory.Delete(Path, recursive: true);
}

public class DocumentTests(PostgresFixture fx) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly TempFolder _folder = new();
    private LocalFileStore Files => new(Options.Create(new StorageOptions { Path = _folder.Path }));

    public void Dispose() => _folder.Dispose();

    private static readonly byte[] PdfBytes = "%PDF-1.7\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();

    private (Scope Scope, DocumentService Documents, EmployeeService Employees) As(string tenant, params string[] roles)
    {
        var scope = fx.NewScope(tenant, roles: roles.Length == 0 ? [Roles.HrAdmin] : roles);
        return (scope, new DocumentService(scope.Db, scope.TenantContext, Files, scope.User, fx.Clock),
            new EmployeeService(scope.Db, scope.TenantContext, new Infrastructure.Persistence.PostgresDocumentNumbers(scope.Db),
                new EmployeeAccess(scope.Db, scope.User), scope.User, fx.Clock));
    }

    private static Task<DocumentDto> Upload(DocumentService documents, Guid employeeId, string fileName, byte[] bytes, DateOnly? expires = null) =>
        documents.UploadAsync(employeeId, new DocumentDetails("Passport", DocumentCategory.Identification, null, expires, null),
            fileName, bytes.Length, new MemoryStream(bytes), default);

    [Fact]
    public async Task Uploaded_files_are_stored_per_company_and_downloaded_with_the_checked_type()
    {
        var co = await fx.CreateTenantAsync();
        var other = await fx.CreateTenantAsync();
        var (scope, documents, employees) = As(co);
        await using var _ = scope;
        var amina = await employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato"), default);

        var doc = await Upload(documents, amina.Id, "../../passport.PDF", PdfBytes, new DateOnly(2027, 3, 1));
        Assert.Equal("passport.PDF", doc.FileName);
        Assert.Equal("application/pdf", doc.ContentType);
        Assert.Equal(98, doc.DaysToExpiry); // 23 Nov 2026 to 1 Mar 2027
        Assert.True(File.Exists(Path.Combine(_folder.Path, co, "employees", amina.Id.ToString("N"), doc.Id.ToString("N"))));

        var download = await documents.DownloadAsync(amina.Id, doc.Id, default);
        await using (download.Content)
        {
            using var copy = new MemoryStream();
            await download.Content.CopyToAsync(copy);
            Assert.Equal(PdfBytes, copy.ToArray());
        }

        // Another company can't see or fetch it, even with the right ids.
        var (otherScope, otherDocuments, _) = As(other);
        await using var __ = otherScope;
        await Assert.ThrowsAsync<NotFoundException>(() => otherDocuments.DownloadAsync(amina.Id, doc.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => otherDocuments.ListAsync(amina.Id, default));
    }

    [Theory]
    [InlineData("setup.exe", "isn't an accepted kind")]
    [InlineData("page.html", "isn't an accepted kind")]
    [InlineData("fake.pdf", "doesn't look like a PDF")] // an executable renamed to .pdf
    [InlineData("photo.png", "doesn't look like a PNG")]
    public async Task Only_real_documents_and_images_are_accepted(string fileName, string error)
    {
        var co = await fx.CreateTenantAsync();
        var (scope, documents, employees) = As(co);
        await using var _ = scope;
        var amina = await employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato"), default);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Upload(documents, amina.Id, fileName, "MZ\x90\0binary"u8.ToArray()));
        Assert.Contains(error, ex.Message);
        Assert.Empty(Directory.EnumerateFiles(_folder.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Deleting_a_document_removes_its_file_and_expiry_dates_must_follow_issue_dates()
    {
        var co = await fx.CreateTenantAsync();
        var (scope, documents, employees) = As(co);
        await using var _ = scope;
        var amina = await employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato"), default);
        var doc = await Upload(documents, amina.Id, "permit.pdf", PdfBytes);

        await Assert.ThrowsAsync<DomainException>(() => documents.UpdateAsync(amina.Id, doc.Id,
            new DocumentDetails("Work permit", DocumentCategory.WorkPermit, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 1), null), default));
        await documents.DeleteAsync(amina.Id, doc.Id, default);
        Assert.Empty(await documents.ListAsync(amina.Id, default));
        Assert.Empty(Directory.EnumerateFiles(_folder.Path, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("demo", "../other/employees/x")]
    [InlineData("demo", "/etc/passwd")]
    [InlineData("demo", "employees/../../other")]
    [InlineData("../other", "employees/x")]
    [InlineData("Demo", "employees/x")]
    public async Task The_file_store_refuses_anything_outside_the_companys_folder(string tenant, string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Files.SaveAsync(tenant, key, new MemoryStream([1])));
        await Assert.ThrowsAsync<ArgumentException>(() => Files.OpenReadAsync(tenant, key));
    }

    // Reminders. "Today" is 23 Nov 2026 (the fixture clock); each test steps its own clock.

    private async Task<(string Tenant, Guid Amina, Guid Brian, Guid Carol)> SeedExpiriesAsync()
    {
        var co = await fx.CreateTenantAsync();
        var (scope, documents, employees) = As(co);
        await using var _ = scope;
        var hired = new DateOnly(2025, 1, 6);
        // Amina: passport expires in 20 days.
        var amina = await employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato"), default);
        await Upload(documents, amina.Id, "passport.pdf", PdfBytes, new DateOnly(2026, 12, 13));
        await Upload(documents, amina.Id, "degree.pdf", PdfBytes); // no expiry: never reminded
        // Brian: fixed-term contract ends in 17 days with nothing after it.
        var brian = await employees.CreateAsync(EmployeeTests.NewEmployee("Brian", "Okello",
            contract: new SaveContractRequest(ContractType.FixedTerm, hired, new DateOnly(2026, 12, 10), null, null)), default);
        // Carol: her contract also ends in 17 days but is renewed, and she's on probation until 1 Dec, not yet confirmed.
        var carol = await employees.CreateAsync(EmployeeTests.NewEmployee("Carol", "Atim",
            contract: new SaveContractRequest(ContractType.FixedTerm, hired, new DateOnly(2026, 12, 10), null, null)), default);
        await employees.AddContractAsync(carol.Id, new SaveContractRequest(ContractType.Permanent, new DateOnly(2026, 12, 11), null,
            new DateOnly(2027, 3, 10), null), default);
        var dave = await employees.CreateAsync(EmployeeTests.NewEmployee("Dave", "Mugisha",
            contract: new SaveContractRequest(ContractType.Permanent, new DateOnly(2026, 9, 1), null, new DateOnly(2026, 12, 1), null)), default);
        return (co, amina.Id, brian.Id, dave.Id);
    }

    private ExpiryService Expiries(Scope scope, IEmailSender email, TimeProvider clock) => new(scope.Db, scope.TenantContext, email, clock);

    [Fact]
    public async Task Expiring_documents_ending_contracts_and_unconfirmed_probations_are_listed_soonest_first()
    {
        var (co, _, _, _) = await SeedExpiriesAsync();
        await using var scope = fx.NewScope(co);
        var items = await Expiries(scope, new FakeEmail(), fx.Clock).ListAsync(60, default);

        Assert.Equal(
            [(ReminderKind.ProbationEnd, "Dave Mugisha", 8), (ReminderKind.ContractEnd, "Brian Okello", 17), (ReminderKind.DocumentExpiry, "Amina Nakato", 20)],
            items.Select(i => (i.Kind, i.EmployeeName, i.DaysLeft)));
        Assert.Equal("Passport", items.Single(i => i.Kind == ReminderKind.DocumentExpiry).Title);
    }

    [Fact]
    public async Task Each_milestone_is_emailed_once_to_the_company_HR_address()
    {
        var (co, _, _, _) = await SeedExpiriesAsync();
        var clock = new TestClock(fx.Clock.GetUtcNow());
        var email = new FakeEmail();
        async Task<ReminderRunResult> Run()
        {
            await using var scope = fx.NewScope(co);
            return await Expiries(scope, email, clock).SendRemindersAsync(default);
        }

        // Today all three are within 30 days: one email lists them all.
        var first = await Run();
        Assert.Equal(3, first.ItemsReminded);
        var message = Assert.Single(email.Sent);
        Assert.Equal([$"hr@{co}.test"], message.To);
        Assert.Contains("Amina Nakato", message.TextBody);
        Assert.Contains("Probation ends (not yet confirmed)", message.TextBody);
        Assert.DoesNotContain("Carol", message.TextBody);

        // Running again the same day sends nothing new.
        Assert.Equal(0, (await Run()).ItemsReminded);

        // Next day Dave's probation reaches 7 days left: that milestone goes out once.
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, (await Run()).ItemsReminded);
        Assert.Contains("Dave Mugisha", email.Sent[^1].TextBody);
        Assert.Equal(0, (await Run()).ItemsReminded);

        // 3 Dec: Brian's contract reaches 7 days and Dave's probation (1 Dec) is overdue; Amina's passport waits until 6 Dec.
        clock.Advance(TimeSpan.FromDays(9));
        Assert.Equal(2, (await Run()).ItemsReminded);
        Assert.Contains("Brian Okello", email.Sent[^1].TextBody);
        Assert.Contains("in 7 days", email.Sent[^1].TextBody);
        Assert.Contains("2 days ago", email.Sent[^1].TextBody);
        Assert.DoesNotContain("Amina", email.Sent[^1].TextBody);
        Assert.Equal(3, email.Sent.Count); // 23 Nov, 24 Nov, 3 Dec
    }

    [Fact]
    public async Task A_late_first_run_sends_only_the_most_urgent_milestone_and_nothing_is_marked_sent_without_mail()
    {
        var (co, _, _, _) = await SeedExpiriesAsync();
        var clock = new TestClock(fx.Clock.GetUtcNow().AddDays(15)); // 8 Dec: Brian 2 days, Amina 5 days, Dave 7 days overdue
        var email = new FakeEmail { Configured = false };
        await using (var scope = fx.NewScope(co))
        {
            var result = await Expiries(scope, email, clock).SendRemindersAsync(default);
            Assert.False(result.MailConfigured);
            Assert.Equal(0, await scope.Db.ReminderLogs.CountAsync());
        }

        email.Configured = true;
        await using (var scope = fx.NewScope(co))
        {
            Assert.Equal(3, (await Expiries(scope, email, clock).SendRemindersAsync(default)).ItemsReminded);
            var logged = await scope.Db.ReminderLogs.Select(r => new { r.Kind, r.DaysBefore }).ToListAsync();
            Assert.All(logged, r => Assert.Equal(r.Kind == ReminderKind.ProbationEnd ? 0 : 7, r.DaysBefore));
            Assert.Contains("7 days ago", email.Sent.Single().TextBody);
        }
    }
}
