using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Application.Payroll;
using TyHr.Domain;
using TyHr.Domain.Exits;
using TyHr.Domain.Leave;
using TyHr.Domain.Payroll;

namespace TyHr.Application.Exits;

public record DuesLineDto(Guid ComponentId, string ComponentName, PayComponentKind Kind, string Label, decimal Amount, string? Basis);

/// <summary>A leaver's final dues: what they're worked out from, the lines, and the payroll they went to.</summary>
public record FinalDuesDto(Guid ExitId, Guid EmployeeId, string EmployeeName, ExitType Type, ExitStatus ExitStatus, DateOnly LastWorkingDay,
    decimal? MonthlyBasic, decimal DailyRate, decimal LeaveDays, int NoticeDays, bool PayInLieu, bool Saved, SettlementStatus Status,
    IReadOnlyList<DuesLineDto> Lines, decimal Earnings, decimal Deductions, Guid? RunId, string? RunName, PayrollStatus? RunStatus,
    IReadOnlyList<string> Notes, bool CanSend, string CurrencyCode);

public record SaveDuesRequest(IReadOnlyList<SettlementLine> Lines);

/// <summary>
/// Works out a leaver's final dues (unused leave, notice in lieu, statutory severance), lets HR or payroll change them,
/// and sends them to an off-cycle payroll for the last working day's month, which is calculated and approved as usual.
/// The last month's salary is the regular payroll's, paid up to the last working day.
/// </summary>
public class FinalDuesService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, PayrollService payroll, TimeProvider clock)
{
    private static readonly (string Code, string Name, string Description)[] Components =
    [
        (FinalDues.LeavePay, "Leave pay on leaving", "Unused annual leave paid out (or leave taken in advance recovered) when someone leaves."),
        (FinalDues.NoticePay, "Pay in lieu of notice", "Notice not worked, paid when someone leaves."),
        (FinalDues.Severance, "Severance pay", "Severance when someone leaves, as the law or their agreement requires."),
    ];

    public async Task<FinalDuesDto> GetAsync(Guid exitId, CancellationToken ct)
    {
        var exit = await ExitAsync(exitId, ct);
        var settlement = await db.FinalSettlements.SingleOrDefaultAsync(s => s.ExitId == exitId, ct);
        if (settlement?.RunId is { } runId && !await db.PayrollRuns.AnyAsync(r => r.Id == runId, ct))
        {
            settlement.PayrollDeleted();
            await db.SaveChangesAsync(ct);
        }
        var (facts, notes) = await FactsAsync(exit, ct);
        var lines = settlement?.Lines ?? (facts == null ? [] : await SuggestAsync(facts, ct));
        return await ToDtoAsync(exit, settlement, facts, lines, notes, ct);
    }

    /// <summary>Saves the lines as HR or payroll want them.</summary>
    public async Task<FinalDuesDto> SaveAsync(Guid exitId, IReadOnlyList<SettlementLine> lines, CancellationToken ct)
    {
        var exit = await ExitAsync(exitId, ct);
        CheckOpen(exit);
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var ids = lines.Select(l => l.ComponentId).Distinct().ToList();
        var components = await db.PayComponents.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        foreach (var l in lines)
        {
            if (!components.TryGetValue(l.ComponentId, out var c)) throw new DomainException("Choose a pay component for each line.");
            if (!c.IsActive) throw new DomainException($"{c.Name} isn't in use.");
            Amounts.Check(l.Amount, country, allowZero: false);
        }
        var settlement = await db.FinalSettlements.SingleOrDefaultAsync(s => s.ExitId == exitId, ct);
        if (settlement == null) db.FinalSettlements.Add(new FinalSettlement(exitId, exit.EmployeeId, lines, user.UserName, clock.GetUtcNow()));
        else settlement.Save(lines, user.UserName, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await GetAsync(exitId, ct);
    }

    /// <summary>Throws away changes and works the lines out again from the current salary, leave and exit details.</summary>
    public async Task<FinalDuesDto> RecalculateAsync(Guid exitId, CancellationToken ct)
    {
        var exit = await ExitAsync(exitId, ct);
        CheckOpen(exit);
        var (facts, _) = await FactsAsync(exit, ct);
        return await SaveAsync(exitId, facts == null ? [] : await SuggestAsync(facts, ct), ct);
    }

    /// <summary>Creates an off-cycle payroll for the last working day's month with these dues as its amounts.</summary>
    public async Task<FinalDuesDto> SendToPayrollAsync(Guid exitId, CancellationToken ct)
    {
        if (!user.Can(Permissions.PayrollPrepare)) throw new DomainException("Payroll sends final dues to a payroll.");
        var exit = await ExitAsync(exitId, ct);
        if (exit.Status is not (ExitStatus.InProgress or ExitStatus.Completed)) throw new DomainException("Final dues are paid once the exit is accepted.");
        var settlement = await db.FinalSettlements.SingleOrDefaultAsync(s => s.ExitId == exitId, ct) ?? throw new DomainException("Save the final dues first.");
        if (settlement.Lines.Count == 0) throw new DomainException("There's nothing to pay.");
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == exit.EmployeeId, ct);
        var today = DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);
        var monthEnd = new DateOnly(exit.LastWorkingDay.Year, exit.LastWorkingDay.Month, 1).AddMonths(1).AddDays(-1);
        await db.InTransactionAsync(async () =>
        {
            var run = await payroll.CreateRunAsync(new CreatePayrollRunRequest(exit.LastWorkingDay.Year, exit.LastWorkingDay.Month, PayrollRunKind.OffCycle,
                $"Final dues: {employee.FullName}", today > monthEnd ? today : monthEnd), ct);
            foreach (var l in settlement.Lines)
                await payroll.AddInputAsync(run.Id, new AddPayrollInputRequest(employee.Id, l.ComponentId, l.Amount, Shorten(l.Basis == null ? l.Label : $"{l.Label}: {l.Basis}")), ct);
            settlement.SentToPayroll(run.Id);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return await GetAsync(exitId, ct);
    }

    private static string Shorten(string note) => note.Length <= 500 ? note : note[..499] + "…";

    private static void CheckOpen(EmployeeExit exit)
    {
        if (exit.Status is ExitStatus.Withdrawn) throw new DomainException("This exit was withdrawn.");
    }

    private async Task<EmployeeExit> ExitAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (!user.Can(Permissions.EmployeesPay)) throw new NotFoundException("There is no such exit.");
        return await db.EmployeeExits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("There is no such exit.");
    }

    private async Task<(DuesFacts? Facts, List<string> Notes)> FactsAsync(EmployeeExit exit, CancellationToken ct)
    {
        var notes = new List<string>();
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == exit.EmployeeId, ct);
        var basic = await db.SalaryChanges.AsNoTracking().Where(s => s.EmployeeId == employee.Id && s.EffectiveDate <= exit.LastWorkingDay)
            .OrderByDescending(s => s.EffectiveDate).Select(s => (decimal?)s.MonthlyBasic).FirstOrDefaultAsync(ct);
        if (basic == null)
        {
            notes.Add($"{employee.FullName} has no salary on record, so nothing can be worked out. Add lines by hand.");
            return (null, notes);
        }
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var profile = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var holidays = await db.PublicHolidays.AsNoTracking().Select(h => h.Date).ToListAsync(ct);
        var calendar = new LeaveCalendar(profile.WorkWeek, holidays, profile.FinancialYearStartMonth);
        decimal leaveDays = 0;
        var annual = await db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Code == "ANNUAL", ct);
        if (annual is { HasBalance: true })
        {
            // Leave starting after the last day is cancelled when the exit completes, so it doesn't count as used.
            var requests = await db.LeaveRequests.AsNoTracking().Where(r => r.EmployeeId == employee.Id && r.StartDate <= exit.LastWorkingDay).ToListAsync(ct);
            var adjustments = await db.LeaveAdjustments.AsNoTracking().Where(a => a.EmployeeId == employee.Id && a.LeaveTypeId == annual.Id).ToListAsync(ct);
            leaveDays = new LeaveBalances(calendar, annual, employee.HireDate, requests, adjustments)
                .For(calendar.LeaveYearOf(exit.LastWorkingDay), exit.LastWorkingDay).Available;
            if (annual.Accrual == LeaveAccrual.Yearly) notes.Add($"{annual.Name} is granted for the whole year, so check the unused days for someone leaving part-way through it.");
        }
        else notes.Add("There's no leave type with the code ANNUAL, so unused leave isn't worked out.");
        var notice = country.Exits.Notice(employee.HireDate, exit.NoticeGivenOn);
        notes.Add("Amounts are before tax: PAYE and other deductions are worked out when the payroll is calculated, by each pay component's settings.");
        return (new DuesFacts(exit.Type, employee.HireDate, exit.NoticeGivenOn, exit.LastWorkingDay, exit.PayInLieu, basic.Value,
            profile.WorkWeek.Days.Count(), leaveDays, notice.Days), notes);
    }

    private async Task<List<SettlementLine>> SuggestAsync(DuesFacts facts, CancellationToken ct)
    {
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var suggestions = FinalDues.Suggest(facts, country);
        var ids = await ComponentsAsync(ct);
        return suggestions.Select(s => new SettlementLine(s.Deduction ? ids[FinalDues.LeaveRecovery] : ids[s.Code], s.Label, s.Amount, s.Basis)).ToList();
    }

    /// <summary>The pay components final dues use, created the first time they're needed (taxable, not pensionable).</summary>
    private async Task<Dictionary<string, Guid>> ComponentsAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, Guid>();
        var codes = Components.Select(c => c.Code).Append(FinalDues.LeaveRecovery).ToList();
        var existing = await db.PayComponents.Where(c => codes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, ct);
        foreach (var (code, name, description) in Components)
        {
            if (!existing.TryGetValue(code, out var c)) db.PayComponents.Add(c = new PayComponent(code, name, PayComponentKind.Earning, true, false, description));
            result[code] = c.Id;
        }
        if (!existing.TryGetValue(FinalDues.LeaveRecovery, out var rec))
            db.PayComponents.Add(rec = new PayComponent(FinalDues.LeaveRecovery, "Leave taken in advance", PayComponentKind.Deduction, false, false,
                "Annual leave taken beyond what was earned, recovered when someone leaves."));
        result[FinalDues.LeaveRecovery] = rec.Id;
        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<FinalDuesDto> ToDtoAsync(EmployeeExit exit, FinalSettlement? settlement, DuesFacts? facts, IReadOnlyList<SettlementLine> lines,
        List<string> notes, CancellationToken ct)
    {
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var name = await db.Employees.AsNoTracking().Where(e => e.Id == exit.EmployeeId).Select(e => e.FirstName + " " + e.LastName).SingleAsync(ct);
        var ids = lines.Select(l => l.ComponentId).Distinct().ToList();
        var components = await db.PayComponents.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var rows = lines.Select(l => new DuesLineDto(l.ComponentId, components.GetValueOrDefault(l.ComponentId)?.Name ?? "?",
            components.GetValueOrDefault(l.ComponentId)?.Kind ?? PayComponentKind.Earning, l.Label, l.Amount, l.Basis)).ToList();
        var run = settlement?.RunId is { } r ? await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r, ct) : null;
        var status = settlement?.Status ?? SettlementStatus.Draft;
        return new FinalDuesDto(exit.Id, exit.EmployeeId, name, exit.Type, exit.Status, exit.LastWorkingDay, facts?.MonthlyBasic,
            facts == null ? 0 : country.Round(FinalDues.DailyRate(facts.MonthlyBasic, facts.WorkingDaysPerWeek)), facts?.LeaveDays ?? 0, facts?.NoticeDays ?? 0,
            exit.PayInLieu, settlement != null, status, rows, rows.Where(x => x.Kind == PayComponentKind.Earning).Sum(x => x.Amount),
            rows.Where(x => x.Kind == PayComponentKind.Deduction).Sum(x => x.Amount), run?.Id, run?.Name, run?.Status, notes,
            settlement != null && status == SettlementStatus.Draft && rows.Count > 0 && exit.Status is ExitStatus.InProgress or ExitStatus.Completed
            && user.Can(Permissions.PayrollPrepare), country.CurrencyCode);
    }
}
