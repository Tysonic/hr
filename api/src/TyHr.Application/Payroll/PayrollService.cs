using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Exits;
using TyHr.Domain.Leave;
using TyHr.Domain.Payroll;

namespace TyHr.Application.Payroll;

/// <summary>
/// Payroll runs from creation to lock: one-off inputs, calculation, the approval steps, payslips, the variance
/// against last month, and reversals. Payroll officers and HR admins prepare; an HR admin who didn't prepare a run
/// approves it.
/// </summary>
public class PayrollService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, TimeProvider clock)
{
    private static readonly PayrollStatus[] Counted = [PayrollStatus.Approved, PayrollStatus.Locked];

    // Runs

    public async Task<IReadOnlyList<PayrollRunDto>> ListRunsAsync(int? year, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var query = db.PayrollRuns.AsNoTracking();
        if (year is { } y) query = query.Where(r => r.Year == y);
        var runs = await query.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenByDescending(r => r.CreatedAt).ToListAsync(ct);
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var reversals = await db.PayrollRuns.AsNoTracking().Where(r => r.ReversesRunId != null)
            .ToDictionaryAsync(r => r.ReversesRunId!.Value, r => r.Id, ct);
        return runs.Select(r => ToDto(r, reversals.TryGetValue(r.Id, out var by) ? by : null, country)).ToList();
    }

    public async Task<PayrollRunDto> GetRunAsync(Guid id, CancellationToken ct)
    {
        var run = await FindRunAsync(id, ct, tracking: false);
        return await ToDtoAsync(run, ct);
    }

    public async Task<PayrollRunDto> CreateRunAsync(CreatePayrollRunRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (r.Kind == PayrollRunKind.Reversal) throw new DomainException("Reverse a locked payroll from the payroll itself.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var month = new DateOnly(r.Year is >= 2000 and <= 2100 ? r.Year : 2000, r.Month is >= 1 and <= 12 ? r.Month : 1, 1);
        if (month.AddMonths(1).AddDays(-1) < country.Payroll.SupportedFrom)
            throw new DomainException($"TyHR's {country.Name} payroll rates start from {country.Payroll.SupportedFrom:MMMM yyyy}.");
        if (r.Kind == PayrollRunKind.Regular && await RegularRunInForceAsync(r.Year, r.Month, ct) is { } existing)
            throw new DomainException($"{existing.Name} already pays {month:MMMM yyyy}. Use an off-cycle payroll for extra payments, or reverse it first.");
        var name = Guard.Optional(r.Name) ?? $"{month:MMMM yyyy}{(r.Kind == PayrollRunKind.OffCycle ? " off-cycle" : "")} payroll";
        var run = new PayrollRun(r.Year, r.Month, r.Kind, name, r.PayDate ?? month.AddMonths(1).AddDays(-1), user.UserName, clock.GetUtcNow());
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(run, ct);
    }

    public async Task<PayrollRunDto> UpdateRunAsync(Guid id, UpdatePayrollRunRequest r, CancellationToken ct)
    {
        var run = await FindRunAsync(id, ct);
        run.Rename(r.Name, r.PayDate);
        await SaveAsync(ct);
        return await ToDtoAsync(run, ct);
    }

    /// <summary>Deletes a run that hasn't been submitted, with its payslips and inputs.</summary>
    public async Task DeleteRunAsync(Guid id, CancellationToken ct)
    {
        var run = await FindRunAsync(id, ct);
        if (run.Status is not (PayrollStatus.Draft or PayrollStatus.Calculated))
            throw new DomainException($"The payroll is {run.Describe()}, so it can't be deleted.");
        db.Payslips.RemoveRange(await db.Payslips.Where(p => p.RunId == id).ToListAsync(ct));
        db.PayrollInputs.RemoveRange(await db.PayrollInputs.Where(i => i.RunId == id).ToListAsync(ct));
        foreach (var dues in await db.FinalSettlements.Where(s => s.RunId == id).ToListAsync(ct)) dues.PayrollDeleted();
        db.PayrollRuns.Remove(run);
        await SaveAsync(ct);
    }

    // Inputs

    public async Task<IReadOnlyList<PayrollInputDto>> InputsAsync(Guid runId, CancellationToken ct)
    {
        await FindRunAsync(runId, ct, tracking: false);
        var inputs = await db.PayrollInputs.AsNoTracking().Where(i => i.RunId == runId).ToListAsync(ct);
        var employees = await db.Employees.AsNoTracking().Where(e => inputs.Select(i => i.EmployeeId).Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeNumber, e.FullName }, ct);
        var components = await db.PayComponents.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        return inputs.Select(i => new PayrollInputDto(i.Id, i.EmployeeId, employees[i.EmployeeId].EmployeeNumber, employees[i.EmployeeId].FullName,
                i.ComponentId, components[i.ComponentId].Name, components[i.ComponentId].Kind, i.Amount, i.Note))
            .OrderBy(i => i.EmployeeName).ThenBy(i => i.Kind).ToList();
    }

    public async Task<IReadOnlyList<PayrollInputDto>> AddInputAsync(Guid runId, AddPayrollInputRequest r, CancellationToken ct)
    {
        var run = await FindRunAsync(runId, ct);
        if (run.Kind == PayrollRunKind.Reversal) throw new DomainException("A reversal only cancels the original payroll; it has no amounts of its own.");
        run.InputsChanged();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == r.EmployeeId, ct) ?? throw new NotFoundException("There is no such employee.");
        if (employee.HireDate > run.PeriodEnd) throw new DomainException($"{employee.FullName} starts after {run.PeriodEnd:d MMMM yyyy}.");
        var component = await db.PayComponents.AsNoTracking().SingleOrDefaultAsync(c => c.Id == r.ComponentId, ct) ?? throw new NotFoundException("There is no such pay component.");
        if (!component.IsActive) throw new DomainException($"{component.Name} isn't in use.");
        db.PayrollInputs.Add(new PayrollInput(runId, r.EmployeeId, r.ComponentId, r.Amount, r.Note, await db.CompanyCountryAsync(tenantContext, ct)));
        await SaveAsync(ct);
        return await InputsAsync(runId, ct);
    }

    public async Task<IReadOnlyList<PayrollInputDto>> RemoveInputAsync(Guid runId, Guid inputId, CancellationToken ct)
    {
        var run = await FindRunAsync(runId, ct);
        run.InputsChanged();
        db.PayrollInputs.Remove(await db.PayrollInputs.SingleOrDefaultAsync(i => i.Id == inputId && i.RunId == runId, ct)
                                ?? throw new NotFoundException("There is no such amount in this payroll."));
        await SaveAsync(ct);
        return await InputsAsync(runId, ct);
    }

    // Calculation

    public async Task<PayrollRunDto> CalculateAsync(Guid id, CancellationToken ct)
    {
        var run = await FindRunAsync(id, ct);
        run.RequireEditable();
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var warnings = new List<string>();
        List<Payslip> payslips;
        if (run.Kind == PayrollRunKind.Reversal)
        {
            payslips = (await db.Payslips.AsNoTracking().Where(p => p.RunId == run.ReversesRunId).ToListAsync(ct))
                .Select(p => Payslip.ReversalOf(run.Id, p)).ToList();
        }
        else
        {
            payslips = await BuildAsync(run, country, warnings, ct);
        }
        db.Payslips.RemoveRange(await db.Payslips.Where(p => p.RunId == id).ToListAsync(ct));
        db.Payslips.AddRange(payslips);
        run.Calculated(payslips, warnings, user.UserName, clock.GetUtcNow());
        await SaveAsync(ct);
        return await ToDtoAsync(run, ct);
    }

    private async Task<List<Payslip>> BuildAsync(PayrollRun run, CountryPack country, List<string> warnings, CancellationToken ct)
    {
        var (start, end) = (run.PeriodStart, run.PeriodEnd);
        var regular = run.Kind == PayrollRunKind.Regular;
        var inputs = (await db.PayrollInputs.AsNoTracking().Where(i => i.RunId == run.Id).ToListAsync(ct)).ToLookup(i => i.EmployeeId);
        var withInputs = inputs.Select(g => g.Key).ToList();

        // Leavers are paid up to their last working day; those who left before the month aren't paid in it.
        var lastDays = (await db.EmployeeExits.AsNoTracking()
                .Where(x => (x.Status == ExitStatus.InProgress || x.Status == ExitStatus.Completed) && x.LastWorkingDay <= end)
                .Select(x => new { x.EmployeeId, x.LastWorkingDay }).ToListAsync(ct))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.Max(x => x.LastWorkingDay));
        var leftThisMonth = lastDays.Where(l => l.Value >= start).Select(l => l.Key).ToList();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => regular ? (e.Status != EmployeeStatus.Exited || leftThisMonth.Contains(e.Id)) && e.HireDate <= end : withInputs.Contains(e.Id))
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync(ct);
        if (regular)
        {
            var gone = employees.Where(e => lastDays.TryGetValue(e.Id, out var l) && l < start).ToList();
            if (gone.Count > 0)
                warnings.Add($"Not paid, as their last working day was before {start:d MMMM}: {string.Join(", ", gone.Select(e => e.FullName))}. Complete their exits.");
            employees = employees.Except(gone).ToList();
        }
        var ids = employees.Select(e => e.Id).ToList();
        var salaries = (await db.SalaryChanges.AsNoTracking().Where(s => ids.Contains(s.EmployeeId) && s.EffectiveDate <= end).ToListAsync(ct))
            .GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.EffectiveDate)!.MonthlyBasic);
        var components = await db.PayComponents.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var items = regular
            ? (await db.EmployeePayItems.AsNoTracking().Where(i => ids.Contains(i.EmployeeId) && i.StartDate <= end && (i.EndDate == null || i.EndDate >= start))
                .ToListAsync(ct)).ToLookup(i => i.EmployeeId)
            : Enumerable.Empty<EmployeePayItem>().ToLookup(i => i.EmployeeId);
        var profiles = await db.EmployeeTaxProfiles.AsNoTracking().Where(p => ids.Contains(p.EmployeeId)).ToDictionaryAsync(p => p.EmployeeId, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var positions = await db.Positions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Title, ct);

        // Unpaid leave in the month, counted in working days.
        var company = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var holidays = await db.PublicHolidays.AsNoTracking().Where(h => h.Date >= start && h.Date <= end).Select(h => h.Date).ToListAsync(ct);
        var calendar = new LeaveCalendar(company.WorkWeek, holidays);
        var workingDays = calendar.CountDays(start, end, false);
        var unpaidTypes = await db.LeaveTypes.AsNoTracking().Where(t => !t.IsPaid).Select(t => t.Id).ToListAsync(ct);
        var unpaidLeave = regular
            ? (await db.LeaveRequests.AsNoTracking().Where(r => ids.Contains(r.EmployeeId) && unpaidTypes.Contains(r.LeaveTypeId)
                && r.Status == LeaveStatus.Approved && r.StartDate <= end && r.EndDate >= start).ToListAsync(ct)).ToLookup(r => r.EmployeeId)
            : Enumerable.Empty<LeaveRequest>().ToLookup(r => r.EmployeeId);

        var earlier = await EarlierPayAsync(run, ids, ct);
        var withoutSalary = new List<string>();
        var payslips = new List<Payslip>();

        foreach (var e in employees)
        {
            var earnings = new List<PayLine>();
            var deductions = new List<PayLine>();
            if (regular)
            {
                if (!salaries.TryGetValue(e.Id, out var basic))
                {
                    withoutSalary.Add(e.FullName);
                    continue;
                }
                DateOnly? lastDay = lastDays.TryGetValue(e.Id, out var l) ? l : null;
                var share = PayCalculator.EmployedShare(e.HireDate, start, end, lastDay);
                var (from, to) = (e.HireDate > start ? e.HireDate : start, lastDay < end ? lastDay.Value : end);
                earnings.Add(new PayLine("BASIC", "Basic salary", PayLineKind.Earning, country.Round(basic * share), true, true,
                    share >= 1 ? null
                    : from > start && to < end ? $"{from:d MMM} to {to:d MMM}: {to.DayNumber - from.DayNumber + 1} of {end.Day} days"
                    : from > start ? $"From {from:d MMM}: {to.DayNumber - from.DayNumber + 1} of {end.Day} days"
                    : $"To {to:d MMM} (last working day): {to.DayNumber - from.DayNumber + 1} of {end.Day} days"));
                foreach (var item in items[e.Id])
                {
                    var c = components[item.ComponentId];
                    if (c.Kind == PayComponentKind.Earning)
                        earnings.Add(new PayLine(c.Code, c.Name, PayLineKind.Earning, country.Round(item.Amount * share), c.Taxable, c.Pensionable, item.Note));
                    else deductions.Add(new PayLine(c.Code, c.Name, PayLineKind.Deduction, item.Amount, Note: item.Note));
                }
                var unpaidDays = unpaidLeave[e.Id].Sum(r => calendar.CountDays(r.StartDate < start ? start : r.StartDate, r.EndDate > end ? end : r.EndDate, false, r.HalfDay));
                if (unpaidDays > 0 && workingDays > 0)
                    earnings.Add(new PayLine("UNPAID", "Unpaid leave", PayLineKind.Earning, -country.Round(Math.Min(basic * share, basic / workingDays * unpaidDays)),
                        true, true, $"{unpaidDays:0.#} of {workingDays:0.#} working days"));
            }
            foreach (var input in inputs[e.Id])
            {
                var c = components[input.ComponentId];
                if (c.Kind == PayComponentKind.Earning) earnings.Add(new PayLine(c.Code, c.Name, PayLineKind.Earning, input.Amount, c.Taxable, c.Pensionable, input.Note));
                else deductions.Add(new PayLine(c.Code, c.Name, PayLineKind.Deduction, input.Amount, Note: input.Note));
            }
            if (earnings.Count == 0 && deductions.Count == 0) continue;

            var profile = profiles.GetValueOrDefault(e.Id);
            var pay = PayCalculator.Calculate(country, new StatutoryContext(end, profile?.Resident ?? true, profile?.SecondaryEmployment ?? false),
                earnings, deductions, earlier.GetValueOrDefault(e.Id) ?? EarlierPay.None);
            var slipWarnings = new List<string>(pay.Warnings);
            if (e.TaxNumber == null) slipWarnings.Add($"No {country.TaxNumberLabel} on the employee's record.");
            if (e.SocialSecurityNumber == null && pay.PensionableGross > 0) slipWarnings.Add($"No {country.SocialSecurityLabel} on the employee's record.");
            if (e.Status == EmployeeStatus.Suspended) slipWarnings.Add("The employee is suspended; check they should be paid.");
            if (e.PaymentMethod == PaymentMethod.Bank && e.BankAccountNumber == null)
                slipWarnings.Add("No bank account on the employee's record, so they won't be in the bank payment file.");
            if (e.PaymentMethod == PaymentMethod.MobileMoney && e.MobileMoneyNumber == null)
                slipWarnings.Add("No mobile money number on the employee's record, so they won't be in the payment file.");
            payslips.Add(new Payslip(run.Id, e, e.DepartmentId is { } d ? departments.GetValueOrDefault(d) : null,
                e.PositionId is { } p ? positions.GetValueOrDefault(p) : null, pay with { Warnings = slipWarnings }));
        }

        if (withoutSalary.Count > 0)
            warnings.Add($"{withoutSalary.Count} {(withoutSalary.Count == 1 ? "person has" : "people have")} no salary and {(withoutSalary.Count == 1 ? "isn't" : "aren't")} paid: {string.Join(", ", withoutSalary.Take(10))}{(withoutSalary.Count > 10 ? "…" : "")}.");
        if (!regular && inputs.Count == 0) warnings.Add("Add the amounts to pay before calculating an off-cycle payroll.");
        var flagged = payslips.Count(p => p.Warnings.Count > 0);
        if (flagged > 0) warnings.Add($"{flagged} payslip{(flagged == 1 ? " has" : "s have")} something to check.");
        return payslips;
    }

    /// <summary>Per employee, what the month's other approved and locked payrolls (including reversals) paid.</summary>
    private async Task<Dictionary<Guid, EarlierPay>> EarlierPayAsync(PayrollRun run, List<Guid> ids, CancellationToken ct)
    {
        var runIds = await db.PayrollRuns.AsNoTracking()
            .Where(r => r.Year == run.Year && r.Month == run.Month && r.Id != run.Id && Counted.Contains(r.Status)).Select(r => r.Id).ToListAsync(ct);
        if (runIds.Count == 0) return [];
        var slips = await db.Payslips.AsNoTracking().Where(p => runIds.Contains(p.RunId) && ids.Contains(p.EmployeeId)).ToListAsync(ct);
        return slips.GroupBy(p => p.EmployeeId).ToDictionary(g => g.Key, g => new EarlierPay(g.Sum(p => p.Gross), g.Sum(p => p.TaxableGross),
            g.Sum(p => p.PensionableGross), g.SelectMany(p => p.Lines).Where(l => l.Kind is PayLineKind.Statutory or PayLineKind.Employer)
                .GroupBy(l => l.Code).ToDictionary(c => c.Key, c => c.Sum(l => l.Amount))));
    }

    // Approval

    public async Task<PayrollRunDto> SubmitAsync(Guid id, CancellationToken ct) => await TransitionAsync(id, r => r.Submit(user.UserName, clock.GetUtcNow()), ct);

    public async Task<PayrollRunDto> ApproveAsync(Guid id, CancellationToken ct) => await TransitionAsync(id, r => r.Approve(user.UserName, clock.GetUtcNow()), ct);

    public async Task<PayrollRunDto> SendBackAsync(Guid id, string note, CancellationToken ct) => await TransitionAsync(id, r => r.SendBack(note), ct);

    public async Task<PayrollRunDto> LockAsync(Guid id, CancellationToken ct) => await TransitionAsync(id, r => r.Lock(user.UserName, clock.GetUtcNow()), ct);

    /// <summary>Starts a reversal of a locked payroll: a run that cancels it line for line, approved like any other.</summary>
    public async Task<PayrollRunDto> ReverseAsync(Guid id, CancellationToken ct)
    {
        var original = await FindRunAsync(id, ct, tracking: false);
        if (original.Kind == PayrollRunKind.Reversal) throw new DomainException("A reversal can't itself be reversed; run the payroll again instead.");
        if (original.Status != PayrollStatus.Locked) throw new DomainException("Only a locked payroll can be reversed. Before that, send it back and change it.");
        if (await db.PayrollRuns.AnyAsync(r => r.ReversesRunId == id, ct)) throw new DomainException($"{original.Name} is already being reversed.");
        var run = new PayrollRun(original.Year, original.Month, PayrollRunKind.Reversal, $"Reversal of {original.Name}", original.PayDate,
            user.UserName, clock.GetUtcNow(), original.Id);
        db.PayrollRuns.Add(run);
        await SaveAsync(ct);
        return await CalculateAsync(run.Id, ct);
    }

    private async Task<PayrollRunDto> TransitionAsync(Guid id, Action<PayrollRun> change, CancellationToken ct)
    {
        var run = await FindRunAsync(id, ct);
        change(run);
        await SaveAsync(ct);
        return await ToDtoAsync(run, ct);
    }

    // Payslips and variance

    public async Task<IReadOnlyList<PayslipSummaryDto>> PayslipsAsync(Guid runId, CancellationToken ct)
    {
        await FindRunAsync(runId, ct, tracking: false);
        return (await db.Payslips.AsNoTracking().Where(p => p.RunId == runId).ToListAsync(ct))
            .OrderBy(p => p.EmployeeName)
            .Select(p => new PayslipSummaryDto(p.Id, p.EmployeeId, p.EmployeeNumber, p.EmployeeName, p.DepartmentName, p.Gross,
                p.Lines.Where(l => l.Code == "PAYE").Sum(l => l.Amount), p.TotalDeductions, p.Net, p.EmployerContributions, p.Warnings))
            .ToList();
    }

    public async Task<PayslipDto> PayslipAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var p = await db.Payslips.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("There is no such payslip.");
        var run = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == p.RunId, ct);
        return ToDto(p, run, (await db.CompanyCountryAsync(tenantContext, ct)).CurrencyCode);
    }

    public static PayslipDto ToDto(Payslip p, PayrollRun run, string currencyCode)
    {
        var paidTo = p.PaymentMethod switch
        {
            PaymentMethod.Bank => $"{p.BankName} {Mask(p.BankAccountNumber)}",
            PaymentMethod.MobileMoney => $"{p.MobileMoneyProvider} {Mask(p.MobileMoneyNumber)}",
            _ => "Cash",
        };
        return new PayslipDto(p.Id, run.Id, run.Name, run.Year, run.Month, run.PayDate, run.Status, p.EmployeeId, p.EmployeeNumber, p.EmployeeName,
            p.DepartmentName, p.PositionTitle, p.TaxNumber, p.SocialSecurityNumber, p.PaymentMethod, paidTo, p.Lines, p.Gross, p.TotalDeductions, p.Net,
            p.EmployerContributions, p.Warnings, currencyCode);

        static string Mask(string? number) => number is { Length: > 4 } n ? $"••••{n[^4..]}" : number ?? "";
    }

    /// <summary>Each person's pay in this run against everything locked for them in the month before.</summary>
    public async Task<PayrollVarianceDto> VarianceAsync(Guid runId, CancellationToken ct)
    {
        var run = await FindRunAsync(runId, ct, tracking: false);
        var previous = run.PeriodStart.AddMonths(-1);
        var previousRuns = await db.PayrollRuns.AsNoTracking()
            .Where(r => r.Year == previous.Year && r.Month == previous.Month && r.Status == PayrollStatus.Locked).Select(r => r.Id).ToListAsync(ct);
        var before = (await db.Payslips.AsNoTracking().Where(p => previousRuns.Contains(p.RunId)).ToListAsync(ct))
            .GroupBy(p => p.EmployeeId).ToDictionary(g => g.Key, g => (Gross: g.Sum(p => p.Gross), Net: g.Sum(p => p.Net), g.First().EmployeeNumber, g.First().EmployeeName));
        var now = (await db.Payslips.AsNoTracking().Where(p => p.RunId == runId).ToListAsync(ct)).ToDictionary(p => p.EmployeeId);

        var rows = new List<VarianceRow>();
        foreach (var id in before.Keys.Union(now.Keys))
        {
            var b = before.TryGetValue(id, out var x) ? x : default((decimal Gross, decimal Net, string EmployeeNumber, string EmployeeName)?);
            var n = now.GetValueOrDefault(id);
            if (n == null && run.Kind != PayrollRunKind.Regular) continue;
            var change = b == null ? "New this month" : n == null ? "Not paid this month" : n.Net != b.Value.Net ? "Pay changed" : null;
            if (change == null) continue;
            var netChange = (n?.Net ?? 0) - (b?.Net ?? 0);
            rows.Add(new VarianceRow(id, n?.EmployeeNumber ?? b!.Value.EmployeeNumber, n?.EmployeeName ?? b!.Value.EmployeeName, change, b?.Gross, n?.Gross,
                b?.Net, n?.Net, netChange, b is { Net: not 0 } ? Math.Round(netChange / b.Value.Net * 100, 1) : null));
        }
        return new PayrollVarianceDto(previous.Year, previous.Month, before.Values.Sum(v => v.Gross), before.Values.Sum(v => v.Net),
            now.Values.Sum(p => p.Gross), now.Values.Sum(p => p.Net), rows.OrderByDescending(r => Math.Abs(r.NetChange)).ToList());
    }

    // Helpers

    /// <summary>The month's regular run that hasn't been reversed, if any.</summary>
    private async Task<PayrollRun?> RegularRunInForceAsync(int year, int month, CancellationToken ct) =>
        await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Year == year && r.Month == month && r.Kind == PayrollRunKind.Regular
            && !db.PayrollRuns.Any(x => x.ReversesRunId == r.Id && x.Status == PayrollStatus.Locked), ct);

    private async Task<PayrollRun> FindRunAsync(Guid id, CancellationToken ct, bool tracking = true)
    {
        tenantContext.RequireTenant();
        var query = tracking ? db.PayrollRuns : db.PayrollRuns.AsNoTracking();
        return await query.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("There is no such payroll.");
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("Someone else changed this payroll at the same time. Reload it and try again.");
        }
    }

    private async Task<PayrollRunDto> ToDtoAsync(PayrollRun run, CancellationToken ct)
    {
        var reversedBy = await db.PayrollRuns.AsNoTracking().Where(r => r.ReversesRunId == run.Id).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        return ToDto(run, reversedBy, await db.CompanyCountryAsync(tenantContext, ct));
    }

    private PayrollRunDto ToDto(PayrollRun r, Guid? reversedBy, CountryPack country)
    {
        var prepare = user.Can(Permissions.PayrollPrepare);
        var approve = user.Can(Permissions.PayrollApprove) && r.Status == PayrollStatus.Submitted
                      && !string.Equals(user.UserName, r.CalculatedBy, StringComparison.OrdinalIgnoreCase)
                      && !string.Equals(user.UserName, r.SubmittedBy, StringComparison.OrdinalIgnoreCase);
        return new PayrollRunDto(r.Id, r.Year, r.Month, r.PeriodStart, r.PeriodEnd, r.Kind, r.Name, r.PayDate, r.Status, r.ReversesRunId,
            reversedBy, r.CreatedBy, r.CreatedAt, r.CalculatedBy, r.CalculatedAt, r.SubmittedBy, r.SubmittedAt,
            r.ApprovedBy, r.ApprovedAt, r.LockedBy, r.LockedAt, r.ReturnNote, r.EmployeeCount, r.Gross, r.Deductions, r.Net, r.EmployerCost, r.Warnings,
            country.CurrencyCode, prepare, approve,
            prepare && r.Status == PayrollStatus.Locked && r.Kind != PayrollRunKind.Reversal && reversedBy == null);
    }
}
