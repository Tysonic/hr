namespace TyHr.Domain.Countries;

/// <summary>The statutory minimum notice an employer gives, by length of service.</summary>
public record NoticeRule(int Days, string Basis, string Source);

/// <summary>A suggested severance payment and how it was worked out.</summary>
public record SeveranceRule(decimal Amount, string Basis, string Source);

/// <summary>A country's rules for ending employment: statutory notice and severance.</summary>
public abstract class ExitRules
{
    /// <summary>Severance suggested for this kind of exit, or null when none is due by law.</summary>
    public abstract SeveranceRule? Severance(Exits.ExitType type, DateOnly hired, DateOnly lastDay, decimal monthlyBasic, CountryPack country);

    /// <summary>The employer's minimum notice for someone hired on <paramref name="hired"/> and given notice on <paramref name="given"/>.</summary>
    public abstract NoticeRule Notice(DateOnly hired, DateOnly given);

    /// <summary>Completed months of service, counting a month only once its day of the month is reached.</summary>
    public static int MonthsOfService(DateOnly hired, DateOnly on)
    {
        if (on < hired) return 0;
        var months = (on.Year - hired.Year) * 12 + on.Month - hired.Month;
        if (on.Day < hired.Day && on.Day < DateTime.DaysInMonth(on.Year, on.Month)) months--;
        return Math.Max(0, months);
    }
}

public sealed class UgandaExitRules : ExitRules
{
    private const string Act = "Employment Act 2006, s.58(3)";

    public override NoticeRule Notice(DateOnly hired, DateOnly given)
    {
        var months = MonthsOfService(hired, given);
        return months switch
        {
            < 6 => new(0, "under 6 months' service: no statutory minimum (check the contract)", Act),
            < 12 => new(14, "6 to 12 months' service: two weeks", Act),
            < 60 => new(30, "1 to 5 years' service: one month", Act),
            < 120 => new(60, "5 to 10 years' service: two months", Act),
            _ => new(90, "10 years' service or more: three months", Act),
        };
    }

    // s.87 makes severance due on death in service (among other cases) after six months' service; s.89 leaves the
    // amount to agreement. One month's pay per completed year is a common basis, so TyHR suggests it, to be checked.
    public override SeveranceRule? Severance(Exits.ExitType type, DateOnly hired, DateOnly lastDay, decimal monthlyBasic, CountryPack country)
    {
        if (type is not (Exits.ExitType.Death or Exits.ExitType.Redundancy)) return null;
        var months = MonthsOfService(hired, lastDay);
        if (months < 6) return null;
        var years = months / 12;
        if (years == 0) return null;
        return new(country.Round(monthlyBasic * years), $"one month's basic pay for each of {years} completed year{(years == 1 ? "" : "s")}; the amount is set by agreement, so check your contract or collective agreement",
            "Employment Act 2006, s.87 and s.89");
    }
}

public sealed class KenyaExitRules : ExitRules
{
    // Monthly-paid staff, which is everyone TyHR pays.
    public override NoticeRule Notice(DateOnly hired, DateOnly given) =>
        new(28, "paid monthly: 28 days", "Employment Act 2007, s.35(1)(c)");

    public override SeveranceRule? Severance(Exits.ExitType type, DateOnly hired, DateOnly lastDay, decimal monthlyBasic, CountryPack country)
    {
        if (type != Exits.ExitType.Redundancy) return null;
        var years = MonthsOfService(hired, lastDay) / 12;
        if (years == 0) return null;
        return new(country.Round(monthlyBasic / 30 * 15 * years), $"15 days' pay (basic ÷ 30) for each of {years} completed year{(years == 1 ? "" : "s")}",
            "Employment Act 2007, s.40(1)(g)");
    }
}
