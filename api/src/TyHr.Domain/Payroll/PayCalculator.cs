using TyHr.Domain.Countries;

namespace TyHr.Domain.Payroll;

/// <summary>What the month's other approved and locked runs already paid someone, so this run adds only the difference.</summary>
public record EarlierPay(decimal Gross, decimal TaxableGross, decimal PensionableGross, IReadOnlyDictionary<string, decimal> Statutory)
{
    public static readonly EarlierPay None = new(0, 0, 0, new Dictionary<string, decimal>());
}

public record PayResult(IReadOnlyList<PayLine> Lines, decimal Gross, decimal TaxableGross, decimal PensionableGross, decimal TotalDeductions, decimal Net,
    decimal EmployerContributions, IReadOnlyList<string> Warnings);

/// <summary>
/// One person's pay for one run. Statutory amounts are worked out on the whole month (this run plus earlier ones)
/// and this run deducts what's still due: a mid-month bonus is taxed at the right band, and a reversal followed by a
/// corrected run comes out right.
/// </summary>
public static class PayCalculator
{
    public static PayResult Calculate(CountryPack country, StatutoryContext context, IReadOnlyList<PayLine> earnings, IReadOnlyList<PayLine> deductions, EarlierPay earlier)
    {
        if (earnings.Any(l => l.Kind != PayLineKind.Earning) || deductions.Any(l => l.Kind != PayLineKind.Deduction))
            throw new ArgumentException("Earnings and deductions are mixed up.");
        var gross = earnings.Sum(l => l.Amount);
        var taxable = earnings.Where(l => l.Taxable).Sum(l => l.Amount);
        var pensionable = earnings.Where(l => l.Pensionable).Sum(l => l.Amount);

        var month = country.Payroll.Calculate(earlier.Gross + gross, earlier.TaxableGross + taxable, earlier.PensionableGross + pensionable, context);
        var statutory = month
            .Select(l => l with { Amount = l.Amount - earlier.Statutory.GetValueOrDefault(l.Code) })
            .Where(l => l.Amount != 0)
            .ToList();
        // Something deducted earlier this month that's no longer due at all (e.g. LST after a reversal) is given back.
        statutory.AddRange(earlier.Statutory.Where(e => e.Value != 0 && month.All(l => l.Code != e.Key))
            .Select(e => new PayLine(e.Key, e.Key, e.Key.EndsWith("_ER") || e.Key == "NITA" ? PayLineKind.Employer : PayLineKind.Statutory, -e.Value)));

        var employeeStatutory = statutory.Where(l => l.Kind == PayLineKind.Statutory).Sum(l => l.Amount);
        var otherDeductions = deductions.Sum(l => l.Amount);
        var net = gross - employeeStatutory - otherDeductions;
        var warnings = new List<string>();
        if (net < 0) warnings.Add($"Net pay is negative ({country.CurrencyCode} {net:N0}): deductions are more than pay.");
        else if (gross > 0 && net < gross * 0.25m && otherDeductions > 0) warnings.Add("Take-home pay is under a quarter of gross; check the deductions.");

        return new PayResult([.. earnings, .. statutory, .. deductions], gross, taxable, pensionable, employeeStatutory + otherDeductions, net,
            statutory.Where(l => l.Kind == PayLineKind.Employer).Sum(l => l.Amount), warnings);
    }

    /// <summary>The share of a month someone was employed for, by calendar days (1 when employed all month).</summary>
    public static decimal EmployedShare(DateOnly hireDate, DateOnly periodStart, DateOnly periodEnd, DateOnly? lastDay = null)
    {
        var from = hireDate > periodStart ? hireDate : periodStart;
        var to = lastDay is { } l && l < periodEnd ? l : periodEnd;
        if (to < from) return 0;
        if (from == periodStart && to == periodEnd) return 1;
        return (decimal)(to.DayNumber - from.DayNumber + 1) / (periodEnd.DayNumber - periodStart.DayNumber + 1);
    }
}
