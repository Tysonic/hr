namespace TyHr.Domain.Leave;

/// <summary>
/// The company's working week, public holidays and leave year, and the day arithmetic on them. Pure: built from data
/// the caller loads, so the rules can be tested without a database.
/// </summary>
public sealed class LeaveCalendar(WorkWeek week, IEnumerable<DateOnly> holidays, int yearStartMonth = 1)
{
    private readonly HashSet<DateOnly> _holidays = [.. holidays];

    public WorkWeek Week { get; } = week;
    public int YearStartMonth { get; } = yearStartMonth is >= 1 and <= 12 ? yearStartMonth : 1;

    public bool IsWorkingDay(DateOnly date) => Week.Includes(date.DayOfWeek) && !_holidays.Contains(date);

    /// <summary>Leave days from start to end inclusive: working days (or every day), half a day for a half-day request.</summary>
    public decimal CountDays(DateOnly start, DateOnly end, bool calendarDays, bool halfDay = false)
    {
        if (end < start) return 0;
        decimal days = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
            if (calendarDays || IsWorkingDay(d)) days++;
        return halfDay ? days / 2 : days;
    }

    /// <summary>The leave year a date falls in, named by the calendar year it starts in.</summary>
    public int LeaveYearOf(DateOnly date) => date.Month >= YearStartMonth ? date.Year : date.Year - 1;

    public (DateOnly Start, DateOnly End) LeaveYear(int year)
    {
        var start = new DateOnly(year, YearStartMonth, 1);
        return (start, start.AddYears(1).AddDays(-1));
    }

    /// <summary>How a request's days fall across leave years (most fall in one).</summary>
    public IReadOnlyDictionary<int, decimal> DaysByLeaveYear(DateOnly start, DateOnly end, bool calendarDays, bool halfDay)
    {
        var result = new Dictionary<int, decimal>();
        for (var year = LeaveYearOf(start); year <= LeaveYearOf(end); year++)
        {
            var (ys, ye) = LeaveYear(year);
            var days = CountDays(start > ys ? start : ys, end < ye ? end : ye, calendarDays, halfDay);
            if (days > 0) result[year] = days;
        }
        return result;
    }
}

/// <summary>The days of the week people normally work, stored as a bit mask (Sunday = 1 ... Saturday = 64).</summary>
public readonly record struct WorkWeek(int Mask)
{
    public static readonly WorkWeek MondayToFriday = Of(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);

    public static WorkWeek Of(params IEnumerable<DayOfWeek> days)
    {
        var mask = days.Aggregate(0, (m, d) => m | (1 << (int)d));
        if (mask == 0) throw new DomainException("The working week needs at least one day.");
        return new WorkWeek(mask);
    }

    public bool Includes(DayOfWeek day) => (Mask & (1 << (int)day)) != 0;
    public IReadOnlyList<DayOfWeek> Days => Enum.GetValues<DayOfWeek>().Where(Includes).ToList();
}
