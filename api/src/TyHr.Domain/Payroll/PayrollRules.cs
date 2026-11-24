namespace TyHr.Domain.Payroll;

public enum PayLineKind { Earning, Deduction, Statutory, Employer }

/// <summary>
/// One line of a payslip. Earnings add to gross (negative for unpaid leave); deductions and statutory lines come off
/// net; employer lines are the company's cost on top (employer NSSF, levies) and aren't paid to the employee.
/// </summary>
public record PayLine(string Code, string Name, PayLineKind Kind, decimal Amount, bool Taxable = false, bool Pensionable = false, string? Note = null)
{
    public PayLine Negated() => this with { Amount = -Amount };
}

/// <summary>What the statutory rules need to know besides the amounts.</summary>
public record StatutoryContext(DateOnly PeriodEnd, bool Resident, bool SecondaryEmployment);

/// <summary>A marginal band: <see cref="Rate"/> applies to the part of income above <see cref="Above"/> (up to the next band).</summary>
public readonly record struct TaxBand(decimal Above, decimal Rate);

/// <summary>A value that applies from a date until the next one.</summary>
public readonly record struct Dated<T>(DateOnly From, T Value);

/// <summary>
/// A country's statutory payroll: income tax and the social contributions and levies on pay. Given a month's totals
/// (all of the month's runs together) it returns the employee's statutory lines and the employer's contributions.
/// Rates are effective-dated tables in each country's class, with where they came from, so a change in the law is a
/// new row, and months before it still calculate as they did.
/// </summary>
public abstract class PayrollRules
{
    /// <summary>The month's statutory lines. Amounts are rounded to the currency; nothing is negative.</summary>
    public abstract IReadOnlyList<PayLine> Calculate(decimal gross, decimal taxableGross, decimal pensionable, StatutoryContext context);

    /// <summary>The earliest month this pack has rates for.</summary>
    public abstract DateOnly SupportedFrom { get; }

    /// <summary>Notes shown to payroll users: what's included and what to check.</summary>
    public abstract IReadOnlyList<string> Notes { get; }

    public static decimal Tax(decimal income, IReadOnlyList<TaxBand> bands)
    {
        var tax = 0m;
        for (var i = 0; i < bands.Count; i++)
        {
            var top = i + 1 < bands.Count ? bands[i + 1].Above : decimal.MaxValue;
            if (income > bands[i].Above) tax += (Math.Min(income, top) - bands[i].Above) * bands[i].Rate;
        }
        return tax;
    }

    /// <summary>The row in force on a date; tables are in date order.</summary>
    protected static T On<T>(IReadOnlyList<Dated<T>> table, DateOnly date, string what)
    {
        for (var i = table.Count - 1; i >= 0; i--)
            if (table[i].From <= date) return table[i].Value;
        throw new DomainException($"TyHR has no {what} rates for {date:MMMM yyyy}.");
    }

    protected void CheckSupported(DateOnly date)
    {
        if (date < SupportedFrom) throw new DomainException($"TyHR's payroll rates start from {SupportedFrom:MMMM yyyy}.");
    }
}

/// <summary>
/// Uganda: PAYE, NSSF and Local Service Tax.
/// <list type="bullet">
/// <item>PAYE on total monthly employment income (NSSF doesn't reduce it). Resident and non-resident tables from
/// URA's PAYE rates page (ura.go.ug, read November 2026: nil to 335,000 for residents, from 1 July 2026 under the
/// Income Tax (Amendment) Act 2026). The table before July 2026 (nil to 235,000) is the long-standing one.
/// Secondary employment is taxed at a flat 30%.</item>
/// <item>NSSF: 5% employee and 10% employer, of gross pay, no ceiling.</item>
/// <item>Local Service Tax: an annual amount by monthly income band (Local Governments Act), taken in four equal
/// instalments July to October. It is not deducted before PAYE here; check this with your tax adviser.</item>
/// </list>
/// </summary>
public sealed class UgandaPayrollRules : PayrollRules
{
    private static readonly IReadOnlyList<Dated<TaxBand[]>> Resident =
    [
        new(new DateOnly(2012, 7, 1), [new(0, 0), new(235_000, 0.10m), new(335_000, 0.20m), new(410_000, 0.30m), new(10_000_000, 0.40m)]),
        new(new DateOnly(2026, 7, 1), [new(0, 0), new(335_000, 0.20m), new(410_000, 0.25m), new(485_000, 0.30m), new(10_000_000, 0.40m)]),
    ];

    private static readonly IReadOnlyList<Dated<TaxBand[]>> NonResident =
    [
        new(new DateOnly(2012, 7, 1), [new(0, 0.10m), new(335_000, 0.20m), new(410_000, 0.30m), new(10_000_000, 0.40m)]),
    ];

    /// <summary>Annual LST by monthly income: (income above, annual amount).</summary>
    private static readonly (decimal Above, decimal Annual)[] LocalServiceTax =
    [
        (0, 0), (100_000, 5_000), (200_000, 10_000), (300_000, 20_000), (400_000, 30_000), (500_000, 40_000),
        (600_000, 60_000), (700_000, 70_000), (800_000, 80_000), (900_000, 90_000), (1_000_000, 100_000),
    ];

    public override DateOnly SupportedFrom { get; } = new(2025, 7, 1);

    public override IReadOnlyList<string> Notes { get; } =
    [
        "PAYE: URA's resident and non-resident tables; from July 2026 income up to UGX 335,000 a month is tax-free for residents.",
        "NSSF: 5% from the employee and 10% from the employer on gross pay.",
        "Local Service Tax: a quarter of the year's amount in each of July to October. Check whether your district needs it handled differently.",
    ];

    public override IReadOnlyList<PayLine> Calculate(decimal gross, decimal taxableGross, decimal pensionable, StatutoryContext c)
    {
        CheckSupported(c.PeriodEnd);
        var lines = new List<PayLine>();
        var paye = c.SecondaryEmployment ? taxableGross * 0.30m
            : Tax(taxableGross, On(c.Resident ? Resident : NonResident, c.PeriodEnd, "PAYE"));
        lines.Add(new("PAYE", "PAYE", PayLineKind.Statutory, R(paye), Note: c.SecondaryEmployment ? "Secondary employment, 30%" : c.Resident ? null : "Non-resident rates"));
        if (pensionable > 0)
        {
            lines.Add(new("NSSF", "NSSF (5%)", PayLineKind.Statutory, R(pensionable * 0.05m)));
            lines.Add(new("NSSF_ER", "Employer NSSF (10%)", PayLineKind.Employer, R(pensionable * 0.10m)));
        }
        if (c.PeriodEnd.Month is >= 7 and <= 10 && gross > 0)
        {
            var annual = LocalServiceTax.Last(b => gross > b.Above).Annual;
            if (annual > 0) lines.Add(new("LST", "Local Service Tax", PayLineKind.Statutory, R(annual / 4), Note: $"UGX {annual:N0} a year, in four parts"));
        }
        return lines;
    }

    private static decimal R(decimal amount) => decimal.Round(Math.Max(0, amount), 0, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Kenya: NSSF, SHIF, Affordable Housing Levy, PAYE and NITA.
/// <list type="bullet">
/// <item>NSSF (NSSF Act 2013): 6% employee and 6% employer, Tier I up to the lower earnings limit and Tier II up to
/// the upper. Limits: 8,000 / 72,000 from February 2025; 9,000 / 108,000 from February 2026 (as published by
/// payroll guides; confirm against NSSF's notice).</item>
/// <item>SHIF: 2.75% of gross, at least KES 300. Housing Levy: 1.5% employee and 1.5% employer, of gross.</item>
/// <item>PAYE: monthly bands 10 / 25 / 30 / 32.5 / 35% on pay after NSSF, SHIF and the Housing Levy (deductible since
/// the Tax Laws (Amendment) Act 2024, 27 December 2024), less personal relief of KES 2,400. Secondary employment:
/// 35% with no relief.</item>
/// <item>NITA: KES 50 a month per employee, paid by the employer.</item>
/// </list>
/// </summary>
public sealed class KenyaPayrollRules : PayrollRules
{
    private static readonly IReadOnlyList<Dated<TaxBand[]>> Paye =
    [
        new(new DateOnly(2023, 7, 1), [new(0, 0.10m), new(24_000, 0.25m), new(32_333, 0.30m), new(500_000, 0.325m), new(800_000, 0.35m)]),
    ];

    private static readonly IReadOnlyList<Dated<(decimal Lower, decimal Upper)>> NssfLimits =
    [
        new(new DateOnly(2025, 2, 1), (8_000, 72_000)),
        new(new DateOnly(2026, 2, 1), (9_000, 108_000)),
    ];

    private const decimal PersonalRelief = 2_400, NssfRate = 0.06m, ShifRate = 0.0275m, ShifMinimum = 300, HousingRate = 0.015m, Nita = 50;

    public override DateOnly SupportedFrom { get; } = new(2025, 2, 1);

    public override IReadOnlyList<string> Notes { get; } =
    [
        "NSSF: 6% employee and 6% employer, Tier I and II; limits KES 9,000 / 108,000 from February 2026. Confirm against NSSF's latest notice.",
        "SHIF 2.75% (at least KES 300) and Housing Levy 1.5% + 1.5% employer, on gross pay.",
        "PAYE on pay after NSSF, SHIF and Housing Levy, less KES 2,400 personal relief. Insurance relief isn't included.",
    ];

    public override IReadOnlyList<PayLine> Calculate(decimal gross, decimal taxableGross, decimal pensionable, StatutoryContext c)
    {
        CheckSupported(c.PeriodEnd);
        var lines = new List<PayLine>();
        var (lower, upper) = On(NssfLimits, c.PeriodEnd, "NSSF");
        var tier1 = R(NssfRate * Math.Min(pensionable, lower));
        var tier2 = R(NssfRate * Math.Max(0, Math.Min(pensionable, upper) - lower));
        var nssf = tier1 + tier2;
        var shif = gross > 0 ? R(Math.Max(ShifMinimum, gross * ShifRate)) : 0;
        var housing = R(gross * HousingRate);
        var taxable = Math.Max(0, taxableGross - nssf - shif - housing);
        var paye = c.SecondaryEmployment ? taxable * 0.35m : Tax(taxable, On(Paye, c.PeriodEnd, "PAYE")) - PersonalRelief;

        lines.Add(new("PAYE", "PAYE", PayLineKind.Statutory, R(paye), Note: c.SecondaryEmployment ? "Secondary employment, 35%" : $"On {taxable:N2} after relief of {PersonalRelief:N0}"));
        if (nssf > 0)
        {
            lines.Add(new("NSSF", "NSSF", PayLineKind.Statutory, nssf, Note: $"Tier I {tier1:N2}, Tier II {tier2:N2}"));
            lines.Add(new("NSSF_ER", "Employer NSSF", PayLineKind.Employer, nssf, Note: $"Tier I {tier1:N2}, Tier II {tier2:N2}"));
        }
        if (shif > 0) lines.Add(new("SHIF", "SHIF", PayLineKind.Statutory, shif));
        if (housing > 0)
        {
            lines.Add(new("AHL", "Housing Levy", PayLineKind.Statutory, housing));
            lines.Add(new("AHL_ER", "Employer Housing Levy", PayLineKind.Employer, housing));
        }
        if (gross > 0) lines.Add(new("NITA", "NITA levy", PayLineKind.Employer, Nita));
        return lines;
    }

    private static decimal R(decimal amount) => decimal.Round(Math.Max(0, amount), 2, MidpointRounding.AwayFromZero);
}
