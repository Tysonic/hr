using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Payroll;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Payroll;
using TyHr.Infrastructure.Pdf;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// What a locked payroll produces: payslip PDFs and emails, employees' own payslips, payment files, statutory
/// returns and the journal. November 2026, Uganda. Amina (bank, 1,000,000 + housing 200,000, SACCO 50,000) and
/// Brian (mobile money, 600,000).
/// </summary>
public class PayrollOutputTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private readonly FakeEmail _email = new();

    private sealed record Services(Scope Scope, PayrollService Payroll, PayrollOutputService Outputs, PayrollSetupService Setup, EmployeeService Employees)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string user, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: user, roles: roles, email: $"{user}@co.test");
        var access = new EmployeeAccess(scope.Db, scope.User);
        return new Services(scope, new PayrollService(scope.Db, scope.TenantContext, scope.User, fx.Clock),
            new PayrollOutputService(scope.Db, scope.TenantContext, scope.User, access, new QuestPayslipRenderer(), _email, fx.Clock, NullLogger<PayrollOutputService>.Instance),
            new PayrollSetupService(scope.Db, scope.TenantContext, fx.Clock),
            new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock));
    }

    private sealed record Company(string Tenant, Guid Run, Guid Amina, Guid Brian);

    private async Task<Company> LockedNovemberAsync(bool lockIt = true)
    {
        var co = await fx.CreateTenantAsync();
        Guid amina, brian;
        await using (var hr = As(co, "grace", Roles.HrAdmin))
        {
            await PayrollDefaults.SeedAsync(hr.Scope.Db, default);
            var hired = new DateOnly(2025, 1, 6);
            amina = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato", email: "amina@co.test",
                salary: new AddSalaryRequest(hired, 1_000_000, "Starting salary")), default)).Id;
            await hr.Employees.UpdatePaymentAsync(amina, new PaymentDetails(PaymentMethod.Bank, "Stanbic", "Kampala Road", "9030012345678", "=HYPERLINK(\"x\")", null, null), default);
            brian = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Brian", "Okello", email: "brian@co.test",
                salary: new AddSalaryRequest(hired, 600_000, "Starting salary")), default)).Id;
            await hr.Employees.UpdatePaymentAsync(brian, new PaymentDetails(PaymentMethod.MobileMoney, null, null, null, null, "MTN MoMo", "0772123456"), default);
            var components = (await hr.Setup.ComponentsAsync(default)).ToDictionary(c => c.Code, c => c.Id);
            await hr.Setup.AddItemAsync(amina, new SavePayItemRequest(components["HOUSING"], 200_000, hired, null, null), default);
            await hr.Setup.AddItemAsync(amina, new SavePayItemRequest(components["SACCO"], 50_000, hired, null, null), default);
        }
        Guid run;
        await using (var paul = As(co, "paul", Roles.PayrollOfficer))
        {
            run = (await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, null, null), default)).Id;
            await paul.Payroll.CalculateAsync(run, default);
            await paul.Payroll.SubmitAsync(run, default);
        }
        await using (var grace = As(co, "grace", Roles.HrAdmin)) await grace.Payroll.ApproveAsync(run, default);
        if (lockIt)
            await using (var paul = As(co, "paul", Roles.PayrollOfficer)) await paul.Payroll.LockAsync(run, default);
        return new Company(co, run, amina, brian);
    }

    [Fact]
    public async Task The_journal_balances_with_costs_by_department_and_whats_owed_to_each_payee()
    {
        var c = await LockedNovemberAsync();
        await using var paul = As(c.Tenant, "paul", Roles.PayrollOfficer);
        var run = await paul.Payroll.GetRunAsync(c.Run, default);
        var lines = await paul.Outputs.JournalLinesAsync((await paul.Scope.Db.PayrollRuns.FindAsync(c.Run))!, default);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        var byAccount = lines.GroupBy(l => l.Account).ToDictionary(g => g.Key, g => g.Sum(l => l.Debit - l.Credit));
        Assert.Equal(1_800_000m, byAccount["Salaries and wages"]);
        Assert.Equal(180_000m, byAccount["Employer payroll contributions"]);       // 10% NSSF
        Assert.Equal(-(90_000m + 180_000m), byAccount["NSSF payable"]);              // 5% + 10%
        Assert.Equal(-50_000m, byAccount["SACCO contribution payable"]);
        Assert.Equal(-run.Net, byAccount["Net salaries payable"]);
        var file = await paul.Outputs.JournalAsync(c.Run, default);
        Assert.EndsWith(".xlsx", file.FileName);
    }

    [Fact]
    public async Task Payment_files_list_each_method_separately_in_full_and_defuse_spreadsheet_formulas()
    {
        var c = await LockedNovemberAsync(lockIt: false); // approved is enough to pay
        await using var paul = As(c.Tenant, "paul", Roles.PayrollOfficer);
        var bank = Encoding.UTF8.GetString((await paul.Outputs.PaymentFileAsync(c.Run, PaymentMethod.Bank, default)).Content).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(l => l.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToList();
        Assert.Equal("Reference,Employee number,Account name,Bank,Branch,Account number,Amount,Currency", bank[0]);
        var amina = Assert.Single(bank.Skip(1));
        Assert.StartsWith("SAL NOV26 EMP-0001,EMP-0001,\"'=HYPERLINK(\"\"x\"\")\",Stanbic,Kampala Road,9030012345678,", amina);
        Assert.EndsWith(",UGX", amina);

        var momo = Encoding.UTF8.GetString((await paul.Outputs.PaymentFileAsync(c.Run, PaymentMethod.MobileMoney, default)).Content).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("MTN MoMo,256772123456,", momo[1]);
        await Assert.ThrowsAsync<DomainException>(() => paul.Outputs.PaymentFileAsync(c.Run, PaymentMethod.Cash, default));
    }

    [Fact]
    public async Task Statutory_returns_total_the_months_approved_payrolls_per_person()
    {
        var c = await LockedNovemberAsync();
        await using var paul = As(c.Tenant, "paul", Roles.PayrollOfficer);
        await Assert.ThrowsAsync<DomainException>(() => paul.Outputs.ReturnsAsync(2026, 10, default));
        var file = await paul.Outputs.ReturnsAsync(2026, 11, default);
        using var book = new XLWorkbook(new MemoryStream(file.Content));
        Assert.Equal(["PAYE", "NSSF"], book.Worksheets.Select(w => w.Name)); // no LST in November
        var paye = book.Worksheet("PAYE");
        Assert.Equal("Amina Nakato", paye.Cell(5, 3).GetString());
        Assert.Equal(248_250m, paye.Cell(5, 6).GetValue<decimal>());   // on 1,200,000
        Assert.Equal("Total", paye.Cell(7, 1).GetString());
        Assert.Equal(248_250m + 68_250m, paye.Cell(7, 6).GetValue<decimal>()); // Brian: 33,750 + 30% of 115,000
        var nssf = book.Worksheet("NSSF");
        Assert.Equal(270_000m, nssf.Cell(7, 7).GetValue<decimal>());
    }

    [Fact]
    public async Task Payslips_are_emailed_once_as_PDFs_after_locking()
    {
        var c = await LockedNovemberAsync(lockIt: false);
        await using (var paul = As(c.Tenant, "paul", Roles.PayrollOfficer))
        {
            Assert.Contains("Lock", (await Assert.ThrowsAsync<DomainException>(() => paul.Outputs.EmailPayslipsAsync(c.Run, default))).Message);
            await paul.Payroll.LockAsync(c.Run, default);
            var result = await paul.Outputs.EmailPayslipsAsync(c.Run, default);
            Assert.Equal((2, 0, true, 0), (result.Sent, result.AlreadySent, result.MailConfigured, result.Remaining));
            var again = await paul.Outputs.EmailPayslipsAsync(c.Run, default);
            Assert.Equal((0, 2), (again.Sent, again.AlreadySent));
        }
        var toAmina = _email.Sent.Single(m => m.To.Contains("amina@co.test"));
        Assert.Contains("November 2026", toAmina.Subject);
        var pdf = Assert.Single(toAmina.Attachments!);
        Assert.Equal("payslip-EMP-0001-2026-11.pdf", pdf.FileName);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf.Content, 0, 4));
    }

    [Fact]
    public async Task Employees_see_only_their_own_payslips_and_only_once_the_payroll_is_locked()
    {
        var c = await LockedNovemberAsync(lockIt: false);
        Guid aminaSlip, brianSlip;
        await using (var paul = As(c.Tenant, "paul", Roles.PayrollOfficer))
        {
            var slips = await paul.Payroll.PayslipsAsync(c.Run, default);
            aminaSlip = slips.Single(s => s.EmployeeId == c.Amina).Id;
            brianSlip = slips.Single(s => s.EmployeeId == c.Brian).Id;
        }
        await using (var amina = As(c.Tenant, "amina", Roles.Employee))
        {
            Assert.Empty(await amina.Outputs.MyPayslipsAsync(default));
            await Assert.ThrowsAsync<NotFoundException>(() => amina.Outputs.MyPayslipAsync(aminaSlip, default));
        }
        await using (var paul = As(c.Tenant, "paul", Roles.PayrollOfficer)) await paul.Payroll.LockAsync(c.Run, default);
        await using (var amina = As(c.Tenant, "amina", Roles.Employee))
        {
            var mine = Assert.Single(await amina.Outputs.MyPayslipsAsync(default));
            Assert.Equal((aminaSlip, 1_200_000m), (mine.Id, mine.Gross));
            Assert.Equal("Stanbic ••••5678", (await amina.Outputs.MyPayslipAsync(aminaSlip, default)).PaidTo);
            Assert.Equal("%PDF", Encoding.ASCII.GetString((await amina.Outputs.MyPayslipPdfAsync(aminaSlip, default)).Content, 0, 4));
            await Assert.ThrowsAsync<NotFoundException>(() => amina.Outputs.MyPayslipAsync(brianSlip, default));
        }
    }
}
