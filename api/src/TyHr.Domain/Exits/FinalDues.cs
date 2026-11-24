using TyHr.Domain.Countries;

namespace TyHr.Domain.Exits;

/// <summary>One line of a leaver's final dues: a pay component (earning or deduction), the amount and how it was worked out.</summary>
public record SettlementLine(Guid ComponentId, string Label, decimal Amount, string? Basis);

/// <summary>What the final dues are worked out from.</summary>
/// <param name="LeaveDays">Unused annual leave at the last working day (negative: taken in advance).</param>
public record DuesFacts(ExitType Type, DateOnly HireDate, DateOnly NoticeGivenOn, DateOnly LastDay, bool PayInLieu, decimal MonthlyBasic,
    int WorkingDaysPerWeek, decimal LeaveDays, int NoticeDays);

/// <summary>A suggested line before it's tied to a pay component.</summary>
public record DuesSuggestion(string Code, string Label, decimal Amount, bool Deduction, string Basis);

/// <summary>
/// Final dues beyond the last month's salary (which the regular payroll pays up to the last working day): unused leave,
/// notice paid in lieu, and statutory severance. Pure arithmetic; HR can change every line before it goes to payroll.
/// </summary>
public static class FinalDues
{
    public const string LeavePay = "LEAVEPAY";
    public const string NoticePay = "NOTICEPAY";
    public const string Severance = "SEVERANCE";
    public const string LeaveRecovery = "LEAVEREC";

    /// <summary>A working day's basic pay: a year's basic over the year's working days.</summary>
    public static decimal DailyRate(decimal monthlyBasic, int workingDaysPerWeek) =>
        workingDaysPerWeek <= 0 ? 0 : monthlyBasic * 12 / (52m * workingDaysPerWeek);

    public static IReadOnlyList<DuesSuggestion> Suggest(DuesFacts f, CountryPack country)
    {
        var lines = new List<DuesSuggestion>();
        var daily = DailyRate(f.MonthlyBasic, f.WorkingDaysPerWeek);
        var rate = $"{country.CurrencyCode} {country.Round(daily).ToString("N" + country.CurrencyMinorUnits)} a working day (basic × 12 ÷ 52 ÷ {f.WorkingDaysPerWeek})";
        if (f.LeaveDays > 0)
            lines.Add(new(LeavePay, "Unused annual leave", country.Round(daily * f.LeaveDays), false, $"{f.LeaveDays:0.#} days at {rate}"));
        else if (f.LeaveDays < 0)
            lines.Add(new(LeavePay, "Annual leave taken in advance", country.Round(daily * -f.LeaveDays), true, $"{-f.LeaveDays:0.#} days at {rate}"));

        var given = f.LastDay.DayNumber - f.NoticeGivenOn.DayNumber;
        var unworked = f.NoticeDays - given;
        if (f.PayInLieu && unworked > 0 && f.Type is not (ExitType.Dismissal or ExitType.Death or ExitType.EndOfContract))
            lines.Add(new(NoticePay, "Pay in lieu of notice", country.Round(f.MonthlyBasic / 30 * unworked), false,
                $"{unworked} of {f.NoticeDays} days' notice not worked, at basic ÷ 30 a day"));

        if (country.Exits.Severance(f.Type, f.HireDate, f.LastDay, f.MonthlyBasic, country) is { } s)
            lines.Add(new(Severance, "Severance pay", s.Amount, false, $"{s.Basis} ({s.Source})"));
        return lines;
    }
}

public enum SettlementStatus { Draft, InPayroll }

/// <summary>A leaver's final dues, prepared by HR or payroll and sent to an off-cycle payroll run to be paid.</summary>
public class FinalSettlement : TenantEntity
{
    private FinalSettlement() { }

    public FinalSettlement(Guid exitId, Guid employeeId, IReadOnlyList<SettlementLine> lines, string by, DateTimeOffset at)
    {
        ExitId = exitId;
        EmployeeId = employeeId;
        Status = SettlementStatus.Draft;
        Save(lines, by, at);
    }

    public Guid ExitId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public List<SettlementLine> Lines { get; private set; } = [];
    public SettlementStatus Status { get; private set; }
    public Guid? RunId { get; private set; }
    public string PreparedBy { get; private set; } = null!;
    public DateTimeOffset PreparedAt { get; private set; }

    public void Save(IReadOnlyList<SettlementLine> lines, string by, DateTimeOffset at)
    {
        if (Status != SettlementStatus.Draft) throw new DomainException("These final dues are already in a payroll. Delete that payroll to change them.");
        if (lines.Count > 30) throw new DomainException("Use up to 30 lines.");
        var cleaned = lines.Select(l => l with { Label = Guard.Required(l.Label, "Description"), Basis = Guard.Optional(l.Basis) }).ToList();
        if (cleaned.Any(l => l.Label.Length > 100 || l.Basis?.Length > 500)) throw new DomainException("Keep descriptions under 100 characters and notes under 500.");
        if (cleaned.Any(l => l.Amount <= 0)) throw new DomainException("Amounts must be more than zero; use a deduction component to take money off.");
        Lines = cleaned;
        PreparedBy = by;
        PreparedAt = at;
    }

    public void SentToPayroll(Guid runId)
    {
        if (Status != SettlementStatus.Draft) throw new DomainException("These final dues are already in a payroll.");
        if (Lines.Count == 0) throw new DomainException("There's nothing to pay.");
        Status = SettlementStatus.InPayroll;
        RunId = runId;
    }

    /// <summary>The payroll it went to was deleted before approval, so it can be changed and sent again.</summary>
    public void PayrollDeleted()
    {
        Status = SettlementStatus.Draft;
        RunId = null;
    }
}
