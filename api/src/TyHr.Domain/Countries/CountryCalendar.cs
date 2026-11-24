using TyHr.Domain.Employees;
using TyHr.Domain.Leave;

namespace TyHr.Domain.Countries;

/// <summary>A public holiday as the country pack proposes it; companies keep their own editable list.</summary>
public record HolidayDefault(DateOnly Date, string Name);

/// <summary>A leave type a country's employment law provides for, offered to new companies as a starting point.</summary>
public record LeaveTypeDefault(string Code, string Name, LeaveAccrual Accrual, decimal? DaysPerYear, decimal? MaxDaysPerRequest,
    decimal CarryForwardMax, bool CountsCalendarDays, bool IsPaid, Gender? EligibleGender, int MinServiceMonths,
    int? DocumentRequiredAfterDays, string Description);

/// <summary>
/// A country's public holidays and statutory leave. Holidays with fixed dates and Easter are generated; holidays set
/// each year by government (Eid, one-off gazetted days) aren't, so HR adds them. Leave defaults reflect the main
/// employment act and are a starting point for HR to confirm against current law and company policy, not legal advice.
/// </summary>
public abstract class CountryCalendar
{
    public abstract IReadOnlyList<HolidayDefault> Holidays(int year);
    public abstract IReadOnlyList<LeaveTypeDefault> LeaveTypes { get; }

    /// <summary>What HR still has to add for a year: holidays whose dates aren't known in advance.</summary>
    public abstract string MovableHolidaysNote { get; }

    /// <summary>Easter Sunday in the Gregorian calendar (anonymous Gregorian algorithm).</summary>
    public static DateOnly EasterSunday(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451, month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    protected static IEnumerable<HolidayDefault> Easter(int year)
    {
        var easter = EasterSunday(year);
        yield return new HolidayDefault(easter.AddDays(-2), "Good Friday");
        yield return new HolidayDefault(easter.AddDays(1), "Easter Monday");
    }

    protected static LeaveTypeDefault Unpaid => new("UNPAID", "Unpaid leave", LeaveAccrual.NoBalance, null, null, 0, false, false, null, 0, null,
        "Agreed time off without pay; deducted in payroll.");
}

/// <summary>Uganda: Employment Act 2006 (ss. 54–57) and the Public Holidays Act.</summary>
public sealed class UgandaCalendar : CountryCalendar
{
    public override IReadOnlyList<HolidayDefault> Holidays(int year) =>
    [
        new(new DateOnly(year, 1, 1), "New Year's Day"),
        new(new DateOnly(year, 1, 26), "NRM Liberation Day"),
        new(new DateOnly(year, 2, 16), "Archbishop Janani Luwum Day"),
        new(new DateOnly(year, 3, 8), "International Women's Day"),
        .. Easter(year),
        new(new DateOnly(year, 5, 1), "Labour Day"),
        new(new DateOnly(year, 6, 3), "Uganda Martyrs' Day"),
        new(new DateOnly(year, 6, 9), "National Heroes' Day"),
        new(new DateOnly(year, 10, 9), "Independence Day"),
        new(new DateOnly(year, 12, 25), "Christmas Day"),
        new(new DateOnly(year, 12, 26), "Boxing Day"),
    ];

    public override string MovableHolidaysNote => "Add Eid al-Fitr and Eid al-Adha once their dates are announced, and any holidays gazetted for the year.";

    public override IReadOnlyList<LeaveTypeDefault> LeaveTypes { get; } =
    [
        new("ANNUAL", "Annual leave", LeaveAccrual.Monthly, 21, null, 5, false, true, null, 0, null,
            "7 working days for every 4 months of continuous service (s. 54). The 5-day carry-forward is a company policy choice."),
        new("SICK", "Sick leave", LeaveAccrual.NoBalance, null, null, 0, false, true, null, 0, 2,
            "Recorded without a balance: the Act provides for wages during the first two months of sickness (s. 55)."),
        new("MATERNITY", "Maternity leave", LeaveAccrual.NoBalance, null, 60, 0, false, true, Gender.Female, 0, null,
            "60 working days with full pay, at least four weeks of it after the birth (s. 56)."),
        new("PATERNITY", "Paternity leave", LeaveAccrual.NoBalance, null, 4, 0, false, true, Gender.Male, 0, null,
            "4 working days immediately after the birth or a miscarriage (s. 57)."),
        Unpaid,
    ];
}

/// <summary>Kenya: Employment Act 2007 (ss. 28–30) and the Public Holidays Act (a holiday on a Sunday moves to Monday).</summary>
public sealed class KenyaCalendar : CountryCalendar
{
    public override IReadOnlyList<HolidayDefault> Holidays(int year)
    {
        HolidayDefault[] fixedDates =
        [
            new(new DateOnly(year, 1, 1), "New Year's Day"),
            new(new DateOnly(year, 5, 1), "Labour Day"),
            new(new DateOnly(year, 6, 1), "Madaraka Day"),
            new(new DateOnly(year, 10, 10), "Mazingira Day"),
            new(new DateOnly(year, 10, 20), "Mashujaa Day"),
            new(new DateOnly(year, 12, 12), "Jamhuri Day"),
            new(new DateOnly(year, 12, 25), "Christmas Day"),
            new(new DateOnly(year, 12, 26), "Boxing Day"),
        ];
        var observed = fixedDates.Select(h => h.Date.DayOfWeek == DayOfWeek.Sunday ? h with { Date = h.Date.AddDays(1), Name = h.Name + " (observed)" } : h).ToList();
        // Christmas on a Sunday moves to Monday, which pushes Boxing Day to Tuesday.
        for (var i = 1; i < observed.Count; i++)
            while (observed.Take(i).Any(h => h.Date == observed[i].Date))
                observed[i] = observed[i] with { Date = observed[i].Date.AddDays(1), Name = observed[i].Name.EndsWith("(observed)") ? observed[i].Name : observed[i].Name + " (observed)" };
        return [.. observed, .. Easter(year)];
    }

    public override string MovableHolidaysNote => "Add Eid al-Fitr once its date is announced, and any holidays gazetted for the year.";

    public override IReadOnlyList<LeaveTypeDefault> LeaveTypes { get; } =
    [
        new("ANNUAL", "Annual leave", LeaveAccrual.Monthly, 21, null, 5, false, true, null, 0, null,
            "21 working days a year with full pay (s. 28). The 5-day carry-forward is a company policy choice."),
        new("SICK", "Sick leave", LeaveAccrual.Yearly, 14, null, 0, false, true, null, 2, 1,
            "After two months' service: 7 days with full pay, then 7 days with half pay, against a medical certificate (s. 30)."),
        new("MATERNITY", "Maternity leave", LeaveAccrual.NoBalance, null, 90, 0, true, true, Gender.Female, 0, null,
            "Three months with full pay (s. 29)."),
        new("PATERNITY", "Paternity leave", LeaveAccrual.NoBalance, null, 14, 0, true, true, Gender.Male, 0, null,
            "Two weeks with full pay (s. 29(8))."),
        Unpaid,
    ];
}
