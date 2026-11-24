using Microsoft.EntityFrameworkCore;
using Npgsql;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Organization;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>Services wired like one HTTP request, for the core-HR tests.</summary>
internal sealed class Hr(Scope scope, TimeProvider clock) : IAsyncDisposable
{
    public Scope Scope { get; } = scope;
    public OrgService Org { get; } = new(scope.Db, scope.TenantContext);
    public EmployeeService Employees { get; } = new(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db),
        new EmployeeAccess(scope.Db, scope.User), scope.User, clock);

    public ValueTask DisposeAsync() => Scope.DisposeAsync();
}

public class OrgTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private Hr As(string tenant, params string[] roles) => new(fx.NewScope(tenant, roles: roles.Length == 0 ? [Roles.HrAdmin] : roles), fx.Clock);

    [Fact]
    public async Task Departments_cannot_form_a_loop()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co);
        var finance = await hr.Org.CreateDepartmentAsync(new("Finance", "FIN", null, null), default);
        var payables = await hr.Org.CreateDepartmentAsync(new("Payables", null, finance.Id, null), default);
        var invoices = await hr.Org.CreateDepartmentAsync(new("Invoices", null, payables.Id, null), default);

        var ex = await Assert.ThrowsAsync<DomainException>(() => hr.Org.UpdateDepartmentAsync(finance.Id, new("Finance", "FIN", invoices.Id, null), default));
        Assert.Contains("own sub-departments", ex.Message);
        Assert.Equal("Finance", (await hr.Org.ListDepartmentsAsync(default)).Single(d => d.Id == payables.Id).ParentName);
    }

    [Fact]
    public async Task Names_are_unique_within_a_company_but_not_across_companies()
    {
        var a = await fx.CreateTenantAsync();
        var b = await fx.CreateTenantAsync();
        await using (var hr = As(a))
        {
            await hr.Org.CreateBranchAsync(new("Kampala", "KLA", null), default);
            var ex = await Assert.ThrowsAsync<DomainException>(() => hr.Org.CreateBranchAsync(new("Kampala", null, null), default));
            Assert.Contains("already a branch called Kampala", ex.Message);
        }
        await using (var hr = As(b))
            Assert.Equal("KLA", (await hr.Org.CreateBranchAsync(new("Kampala", "kla", null), default)).Code);
    }

    [Fact]
    public async Task Things_in_use_cannot_be_deleted_or_deactivated_only_unused_ones()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co);
        var branch = await hr.Org.CreateBranchAsync(new("Head office", null, null), default);
        var spare = await hr.Org.CreateBranchAsync(new("Spare", null, null), default);
        await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato", branchId: branch.Id), default);

        await Assert.ThrowsAsync<DomainException>(() => hr.Org.DeleteBranchAsync(branch.Id, default));
        await Assert.ThrowsAsync<DomainException>(() => hr.Org.SetBranchActiveAsync(branch.Id, false, default));
        await hr.Org.DeleteBranchAsync(spare.Id, default);
        Assert.DoesNotContain(await hr.Org.ListBranchesAsync(default), x => x.Id == spare.Id);
        Assert.Equal(1, (await hr.Org.ListBranchesAsync(default)).Single(x => x.Id == branch.Id).EmployeeCount);
    }

    [Fact]
    public async Task Grade_salary_ranges_follow_the_currency_and_positions_need_an_active_department()
    {
        var co = await fx.CreateTenantAsync(); // Uganda: UGX has no decimal places
        await using var hr = As(co);
        await Assert.ThrowsAsync<DomainException>(() => hr.Org.CreateJobGradeAsync(new("G1", "Entry", 1, 500_000.50m, 900_000m), default));
        await Assert.ThrowsAsync<DomainException>(() => hr.Org.CreateJobGradeAsync(new("G1", "Entry", 1, 900_000m, 500_000m), default));
        var grade = await hr.Org.CreateJobGradeAsync(new("g1", "Entry", 1, 500_000m, 900_000m), default);
        Assert.Equal("G1", grade.Code);

        var ops = await hr.Org.CreateDepartmentAsync(new("Operations", null, null, null), default);
        await hr.Org.SetDepartmentActiveAsync(ops.Id, false, default);
        var ex = await Assert.ThrowsAsync<DomainException>(() => hr.Org.CreatePositionAsync(new("Driver", ops.Id, grade.Id), default));
        Assert.Contains("active department", ex.Message);
    }
}

public class EmployeeTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private static readonly DateOnly Hired = new(2025, 1, 6);

    private Hr As(string tenant, string[]? roles = null, string? email = null) =>
        new(fx.NewScope(tenant, roles: roles ?? [Roles.HrAdmin], email: email), fx.Clock);

    internal static CreateEmployeeRequest NewEmployee(string first, string last, Guid? managerId = null, Guid? branchId = null,
        string? email = null, string? nin = null, AddSalaryRequest? salary = null, SaveContractRequest? contract = null) =>
        new(new PersonalDetails(first, null, last, Gender.Female, new DateOnly(1990, 5, 1), null, "UG",
                nin == null ? null : IdType.NationalId, nin, null, null, null, null, null),
            new EmploymentDetails(Hired, email, null, branchId, null, null, managerId),
            contract, salary);

    [Fact]
    public async Task Employees_are_numbered_per_company()
    {
        var a = await fx.CreateTenantAsync();
        var b = await fx.CreateTenantAsync();
        await using var hrA = As(a);
        await using var hrB = As(b);
        Assert.Equal("EMP-0001", (await hrA.Employees.CreateAsync(NewEmployee("Amina", "Nakato"), default)).EmployeeNumber);
        Assert.Equal("EMP-0002", (await hrA.Employees.CreateAsync(NewEmployee("Brian", "Okello"), default)).EmployeeNumber);
        Assert.Equal("EMP-0001", (await hrB.Employees.CreateAsync(NewEmployee("Carol", "Atim"), default)).EmployeeNumber);
    }

    [Fact]
    public async Task ID_and_account_numbers_are_encrypted_in_the_database_and_kept_out_of_the_audit_trail()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co);
        var created = await hr.Employees.CreateAsync(NewEmployee("Amina", "Nakato", nin: "cm 9001 2345 abcd"), default);
        await hr.Employees.UpdatePaymentAsync(created.Id, new PaymentDetails(PaymentMethod.Bank, "Stanbic", "Garden City", "9030 0123 4567",
            "Amina Nakato", null, null), default);

        var read = await hr.Employees.GetAsync(created.Id, default);
        Assert.Equal("CM90012345ABCD", read.Personal!.IdNumber);
        Assert.Equal("903001234567", read.Payment!.BankAccountNumber);

        await using var raw = new NpgsqlConnection(fx.ConnectionString);
        await raw.OpenAsync();
        await using var cmd = new NpgsqlCommand("""SELECT "IdNumber", "BankAccountNumber" FROM "Employees" WHERE "Id" = @id""", raw);
        cmd.Parameters.AddWithValue("id", created.Id);
        await using (var row = await cmd.ExecuteReaderAsync())
        {
            await row.ReadAsync();
            Assert.StartsWith("v1:", row.GetString(0));
            Assert.StartsWith("v1:", row.GetString(1));
            Assert.DoesNotContain("CM90012345ABCD", row.GetString(0));
        }

        var audit = await hr.Scope.Db.AuditEntries.Where(e => e.Tenant == co && e.EntityType == "Employee").Select(e => e.Changes).ToListAsync();
        Assert.NotEmpty(audit);
        Assert.All(audit, changes => Assert.DoesNotContain("CM90012345ABCD", changes));
        Assert.All(audit, changes => Assert.DoesNotContain("903001234567", changes));
    }

    [Fact]
    public async Task National_IDs_follow_the_country_format_but_passports_dont()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co);
        var ex = await Assert.ThrowsAsync<DomainException>(() => hr.Employees.CreateAsync(NewEmployee("Amina", "Nakato", nin: "12345"), default));
        Assert.Contains("not a valid NIN", ex.Message);

        var foreign = NewEmployee("John", "Smith") with
        {
            Personal = NewEmployee("John", "Smith").Personal with { Nationality = "GB", IdType = IdType.Passport, IdNumber = "533380006" },
        };
        Assert.Equal("533380006", (await hr.Employees.CreateAsync(foreign, default)).Personal!.IdNumber);
    }

    [Fact]
    public async Task A_reporting_line_cannot_loop_and_positions_must_match_the_department()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co);
        var ceo = await hr.Employees.CreateAsync(NewEmployee("Grace", "Achieng"), default);
        var cfo = await hr.Employees.CreateAsync(NewEmployee("Peter", "Mugisha", managerId: ceo.Id), default);
        var accountant = await hr.Employees.CreateAsync(NewEmployee("Sarah", "Namuli", managerId: cfo.Id), default);

        var loop = await Assert.ThrowsAsync<DomainException>(() =>
            hr.Employees.UpdateEmploymentAsync(ceo.Id, new EmploymentDetails(Hired, null, null, null, null, null, accountant.Id), default));
        Assert.Contains("loop", loop.Message);

        var finance = await hr.Org.CreateDepartmentAsync(new("Finance", null, null, null), default);
        var sales = await hr.Org.CreateDepartmentAsync(new("Sales", null, null, null), default);
        var rep = await hr.Org.CreatePositionAsync(new("Sales rep", sales.Id, null), default);
        await Assert.ThrowsAsync<DomainException>(() =>
            hr.Employees.UpdateEmploymentAsync(accountant.Id, new EmploymentDetails(Hired, null, null, null, finance.Id, rep.Id, cfo.Id), default));
    }

    [Fact]
    public async Task A_line_manager_sees_only_their_team_and_not_personal_or_pay_details()
    {
        var co = await fx.CreateTenantAsync();
        Guid boss, lead, member, outsider;
        await using (var hr = As(co))
        {
            boss = (await hr.Employees.CreateAsync(NewEmployee("Grace", "Achieng"), default)).Id;
            lead = (await hr.Employees.CreateAsync(NewEmployee("Peter", "Mugisha", boss, email: "Peter@Co.test"), default)).Id;
            member = (await hr.Employees.CreateAsync(NewEmployee("Sarah", "Namuli", lead, nin: "CF90012345ABCD",
                salary: new AddSalaryRequest(Hired, 1_500_000m, "Starting salary")), default)).Id;
            var deeper = (await hr.Employees.CreateAsync(NewEmployee("Tom", "Ssali", member), default)).Id;
            outsider = (await hr.Employees.CreateAsync(NewEmployee("Ruth", "Akello", boss), default)).Id;
        }

        await using var manager = As(co, [Roles.LineManager], email: "peter@co.test");
        var visible = await manager.Employees.ListAsync(new EmployeeQuery(), default);
        Assert.Equal(["Sarah Namuli", "Tom Ssali"], visible.Items.Select(i => i.FullName).Order());
        await Assert.ThrowsAsync<NotFoundException>(() => manager.Employees.GetAsync(outsider, default));
        await Assert.ThrowsAsync<NotFoundException>(() => manager.Employees.GetAsync(boss, default));

        var record = await manager.Employees.GetAsync(member, default);
        Assert.Null(record.Personal);
        Assert.Null(record.Payment);
        Assert.Null(record.Salaries);
        Assert.Null(record.NextOfKin);
        Assert.False(record.Access.CanEdit);

        // A manager whose sign-in matches no employee record sees nobody.
        await using var stranger = As(co, [Roles.LineManager], email: "nobody@co.test");
        Assert.Equal(0, (await stranger.Employees.ListAsync(new EmployeeQuery(), default)).TotalCount);
    }

    [Fact]
    public async Task HR_officers_see_personal_details_but_only_payroll_and_HR_admins_see_pay()
    {
        var co = await fx.CreateTenantAsync();
        Guid id;
        await using (var hr = As(co))
            id = (await hr.Employees.CreateAsync(NewEmployee("Amina", "Nakato", nin: "CF90012345ABCD",
                salary: new AddSalaryRequest(Hired, 2_000_000m, "Starting salary")), default)).Id;

        await using var officer = As(co, [Roles.HrOfficer]);
        var seen = await officer.Employees.GetAsync(id, default);
        Assert.Equal("CF90012345ABCD", seen.Personal!.IdNumber);
        Assert.Null(seen.Salaries);
        Assert.Null(seen.Payment);

        await using var payroll = As(co, [Roles.PayrollOfficer]);
        Assert.Equal(2_000_000m, (await payroll.Employees.GetAsync(id, default)).Salaries!.Single().MonthlyBasic);

        // Setting a salary when creating someone needs pay access.
        await Assert.ThrowsAsync<DomainException>(() => officer.Employees.CreateAsync(NewEmployee("Brian", "Okello",
            salary: new AddSalaryRequest(Hired, 1m, "x")), default));
    }

    [Fact]
    public async Task Salary_history_is_effective_dated_and_only_future_changes_can_be_removed()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co);
        var grade = await hr.Org.CreateJobGradeAsync(new("G2", "Officer", 2, 1_000_000m, 2_000_000m), default);
        var dept = await hr.Org.CreateDepartmentAsync(new("Finance", null, null, null), default);
        var position = await hr.Org.CreatePositionAsync(new("Accountant", dept.Id, grade.Id), default);
        var e = await hr.Employees.CreateAsync(NewEmployee("Amina", "Nakato", salary: new AddSalaryRequest(Hired, 1_200_000m, "Starting salary"))
            with { Employment = new EmploymentDetails(Hired, null, null, null, dept.Id, position.Id, null) }, default);

        await Assert.ThrowsAsync<DomainException>(() => hr.Employees.AddSalaryAsync(e.Id, new(Hired.AddDays(-1), 1_000_000m, "Too early"), default));
        await Assert.ThrowsAsync<DomainException>(() => hr.Employees.AddSalaryAsync(e.Id, new(new DateOnly(2026, 1, 1), 1_300_000.5m, "Decimals"), default));
        await hr.Employees.AddSalaryAsync(e.Id, new(new DateOnly(2026, 7, 1), 1_500_000m, "Promotion"), default);
        var withFuture = await hr.Employees.AddSalaryAsync(e.Id, new(new DateOnly(2027, 1, 1), 2_500_000m, "Annual review"), default);

        // Today is 2026-11-23 (the test clock): the July change is current; January's is ahead and above the grade.
        Assert.Equal(1_500_000m, withFuture.Salaries!.Single(s => s.IsCurrent).MonthlyBasic);
        var future = withFuture.Salaries!.Single(s => s.EffectiveDate == new DateOnly(2027, 1, 1));
        Assert.True(future.OutsideGrade);
        Assert.Equal("tester", future.RecordedBy);

        var past = withFuture.Salaries!.Single(s => s.IsCurrent);
        await Assert.ThrowsAsync<DomainException>(() => hr.Employees.DeleteSalaryAsync(e.Id, past.Id, default));
        Assert.Equal(2, (await hr.Employees.DeleteSalaryAsync(e.Id, future.Id, default)).Salaries!.Count);
    }

    [Fact]
    public async Task Contracts_cannot_overlap_and_fixed_terms_need_an_end()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co);
        var e = await hr.Employees.CreateAsync(NewEmployee("Amina", "Nakato",
            contract: new SaveContractRequest(ContractType.FixedTerm, Hired, new DateOnly(2025, 12, 31), new DateOnly(2025, 4, 5), null)), default);

        await Assert.ThrowsAsync<DomainException>(() =>
            hr.Employees.AddContractAsync(e.Id, new(ContractType.FixedTerm, new DateOnly(2026, 1, 1), null, null, null), default));
        await Assert.ThrowsAsync<DomainException>(() =>
            hr.Employees.AddContractAsync(e.Id, new(ContractType.Permanent, new DateOnly(2025, 6, 1), null, null, null), default));

        var renewed = await hr.Employees.AddContractAsync(e.Id, new(ContractType.Permanent, new DateOnly(2026, 1, 1), null, null, "Made permanent"), default);
        Assert.Equal(ContractType.Permanent, renewed.Contracts.Single(c => c.IsCurrent).Type);

        var first = renewed.Contracts.Single(c => c.Type == ContractType.FixedTerm);
        var confirmed = await hr.Employees.ConfirmContractAsync(e.Id, first.Id, new DateOnly(2025, 4, 7), default);
        Assert.Equal(new DateOnly(2025, 4, 7), confirmed.Contracts.Single(c => c.Id == first.Id).ConfirmedOn);
    }

    [Fact]
    public async Task Another_companys_employees_are_invisible_and_the_directory_shows_only_colleagues()
    {
        var a = await fx.CreateTenantAsync();
        var b = await fx.CreateTenantAsync();
        Guid id;
        await using (var hr = As(a))
            id = (await hr.Employees.CreateAsync(NewEmployee("Amina", "Nakato", email: "amina@a.test"), default)).Id;

        await using var other = As(b);
        await Assert.ThrowsAsync<NotFoundException>(() => other.Employees.GetAsync(id, default));
        Assert.Equal(0, (await other.Employees.DirectoryAsync("Amina", null, 1, 50, default)).TotalCount);

        await using var colleague = As(a, [Roles.Employee]);
        var found = await colleague.Employees.DirectoryAsync("nakato", null, 1, 50, default);
        Assert.Equal("amina@a.test", found.Items.Single().WorkEmail);
        Assert.Equal(0, (await colleague.Employees.ListAsync(new EmployeeQuery(), default)).TotalCount);
    }
}
