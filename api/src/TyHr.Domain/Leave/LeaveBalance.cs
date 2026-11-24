using TyHr.Domain.Employees;

namespace TyHr.Domain.Leave;

/// <summary>One leave year's balance of one type for one employee.</summary>
/// <param name="Earned">Earned so far (Monthly accrual up to the as-of date; Yearly: the year's days, pro rata).</param>
/// <param name="FullYear">What the year will have earned by its end (or by leaving, if earlier).</param>
/// <param name="Available">Earned + carried + adjusted − taken − pending.</param>
public record LeaveBalance(int Year, decimal Earned, decimal FullYear, decimal CarriedForward, decimal Adjusted, decimal Taken, decimal Pending, decimal Available);

/// <summary>
/// Works out balances from the type's rules and the employee's history. Rules:
/// <list type="bullet">
/// <item>Service counts from the hire date; nothing is earned before <see cref="LeaveType.MinServiceMonths"/> have passed
/// (then it counts from the hire date, so the waiting months aren't lost).</item>
/// <item>Monthly: DaysPerYear/12 at each month end of service; the month of hiring counts if hired by the 15th.</item>
/// <item>Yearly: the whole year's days, pro rata by months of service in the year someone joins (same 15th rule).</item>
/// <item>Carry-forward: up to CarryForwardMax of the previous year's unused days at its end (pending days count as used).</item>
/// </list>
/// Earned amounts are rounded down to half days.
/// </summary>
public sealed class LeaveBalances(LeaveCalendar calendar, LeaveType type, DateOnly hireDate,
    IReadOnlyList<LeaveRequest> requests, IReadOnlyList<LeaveAdjustment> adjustments)
{
    /// <summary>The balance for a leave year as of a date (accrual up to that date).</summary>
    public LeaveBalance For(int year, DateOnly asOf)
    {
        if (!type.HasBalance) throw new InvalidOperationException($"{type.Name} has no balance.");
        var earned = Earned(year, asOf);
        var fullYear = Earned(year, calendar.LeaveYear(year).End);
        var carried = CarriedInto(year);
        var adjusted = adjustments.Where(a => a.LeaveYear == year).Sum(a => a.Days);
        var taken = Used(year, LeaveStatus.Approved);
        var pending = Used(year, LeaveStatus.Pending);
        return new LeaveBalance(year, earned, fullYear, carried, adjusted, taken, pending, earned + carried + adjusted - taken - pending);
    }

    public bool Eligible(DateOnly on) => on >= hireDate.AddMonths(type.MinServiceMonths);

    private decimal Earned(int year, DateOnly asOf)
    {
        var (start, end) = calendar.LeaveYear(year);
        if (asOf < start || hireDate > end || !Eligible(asOf < end ? asOf : end)) return 0;
        // Months of service in the year: from the hire month (if hired by the 15th, else the month after) or the year start.
        var firstMonth = hireDate <= start ? start : new DateOnly(hireDate.Year, hireDate.Month, 1).AddMonths(hireDate.Day <= 15 ? 0 : 1);
        int MonthsUpTo(DateOnly until)
        {
            var months = 0;
            for (var m = firstMonth; m <= end && m.AddMonths(1).AddDays(-1) <= until; m = m.AddMonths(1)) months++;
            return months;
        }
        var perYear = type.DaysPerYear!.Value;
        var amount = type.Accrual switch
        {
            LeaveAccrual.Monthly => perYear * MonthsUpTo(asOf) / 12,
            LeaveAccrual.Yearly => perYear * MonthsUpTo(end) / 12,
            _ => 0,
        };
        return Math.Floor(amount * 2) / 2;
    }

    private decimal CarriedInto(int year)
    {
        if (type.CarryForwardMax <= 0) return 0;
        var previous = year - 1;
        if (calendar.LeaveYear(previous).End < hireDate) return 0;
        var closing = For(previous, calendar.LeaveYear(previous).End);
        return Math.Min(type.CarryForwardMax, Math.Max(0, closing.Available));
    }

    /// <summary>
    /// Days used in a year: each request's stored days (so later holiday edits don't change approved leave), split in
    /// proportion when a request crosses into another leave year.
    /// </summary>
    private decimal Used(int year, LeaveStatus status) => requests
        .Where(r => r.Status == status && r.LeaveTypeId == type.Id)
        .Sum(r =>
        {
            if (calendar.LeaveYearOf(r.StartDate) == calendar.LeaveYearOf(r.EndDate))
                return calendar.LeaveYearOf(r.StartDate) == year ? r.Days : 0;
            var split = calendar.DaysByLeaveYear(r.StartDate, r.EndDate, type.CountsCalendarDays, r.HalfDay);
            var total = split.Values.Sum();
            return total == 0 ? 0 : r.Days * split.GetValueOrDefault(year) / total;
        });
}
