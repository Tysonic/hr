using TyHr.Domain.Countries;
using TyHr.Domain.Leave;

namespace TyHr.Tests;

/// <summary>The leave arithmetic, without a database.</summary>
public class LeaveRulesTests
{
    private static DateOnly D(string s) => DateOnly.Parse(s);

    [Theory]
    [InlineData(2024, "2024-03-31")]
    [InlineData(2025, "2025-04-20")]
    [InlineData(2026, "2026-04-05")]
    [InlineData(2027, "2027-03-28")]
    public void Easter_is_worked_out_for_any_year(int year, string easter) => Assert.Equal(D(easter), CountryCalendar.EasterSunday(year));

    [Fact]
    public void Kenyan_holidays_on_a_Sunday_move_to_Monday_and_Boxing_Day_moves_past_Christmas()
    {
        var y2022 = new KenyaCalendar().Holidays(2022).ToDictionary(h => h.Date, h => h.Name);
        Assert.Equal("Christmas Day (observed)", y2022[D("2022-12-26")]); // 25 Dec 2022 was a Sunday
        Assert.Equal("Boxing Day (observed)", y2022[D("2022-12-27")]);
        Assert.Equal("Good Friday", y2022[D("2022-04-15")]);
        Assert.Equal("Madaraka Day", new KenyaCalendar().Holidays(2026).Single(h => h.Date == D("2026-06-01")).Name);
        // Uganda has no such rule: Christmas stays on the Sunday.
        Assert.Contains(new UgandaCalendar().Holidays(2022), h => h.Date == D("2022-12-25"));
    }

    [Fact]
    public void Leave_days_skip_weekends_and_holidays_unless_the_type_counts_calendar_days()
    {
        var calendar = new LeaveCalendar(WorkWeek.MondayToFriday, [D("2026-12-25")]);
        Assert.Equal(4, calendar.CountDays(D("2026-12-21"), D("2026-12-27"), calendarDays: false)); // Mon–Sun with Christmas on Friday
        Assert.Equal(7, calendar.CountDays(D("2026-12-21"), D("2026-12-27"), calendarDays: true));
        Assert.Equal(0.5m, calendar.CountDays(D("2026-12-21"), D("2026-12-21"), false, halfDay: true));

        var sixDays = new LeaveCalendar(WorkWeek.Of(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday), []);
        Assert.Equal(6, sixDays.CountDays(D("2026-12-21"), D("2026-12-27"), false));
    }

    [Fact]
    public void Leave_years_can_follow_a_financial_year_and_requests_split_across_them()
    {
        var calendar = new LeaveCalendar(WorkWeek.MondayToFriday, [], yearStartMonth: 7);
        Assert.Equal(2025, calendar.LeaveYearOf(D("2026-06-30")));
        Assert.Equal(2026, calendar.LeaveYearOf(D("2026-07-01")));
        Assert.Equal((D("2026-07-01"), D("2027-06-30")), calendar.LeaveYear(2026));
        var split = calendar.DaysByLeaveYear(D("2026-06-29"), D("2026-07-03"), false, false); // Mon–Fri
        Assert.Equal(2, split[2025]);
        Assert.Equal(3, split[2026]);
    }

    private static LeaveType Annual(LeaveAccrual accrual = LeaveAccrual.Monthly, decimal carry = 5, int minService = 0) =>
        new("ANNUAL", "Annual leave", accrual, 21, null, carry, false, true, null, minService, null, null);

    [Theory]
    [InlineData("2025-01-06", "2026-06-30", 10.5)] // a full year before: 6 months of 1.75
    [InlineData("2026-01-10", "2026-06-30", 10.5)] // hired by the 15th: January counts
    [InlineData("2026-01-20", "2026-06-30", 8.5)]  // after the 15th: from February, 5 × 1.75 = 8.75, rounded down to a half day
    [InlineData("2026-01-10", "2026-06-29", 8.5)]  // June hasn't ended
    [InlineData("2026-08-01", "2026-06-30", 0)]    // not hired yet
    public void Monthly_leave_is_earned_at_each_month_end(string hired, string asOf, decimal expected)
    {
        var balances = new LeaveBalances(new LeaveCalendar(WorkWeek.MondayToFriday, []), Annual(), D(hired), [], []);
        Assert.Equal(expected, balances.For(2026, D(asOf)).Earned);
    }

    [Fact]
    public void Yearly_leave_is_granted_up_front_pro_rata_in_the_joining_year_after_any_qualifying_service()
    {
        var calendar = new LeaveCalendar(WorkWeek.MondayToFriday, []);
        var sick = new LeaveType("SICK", "Sick leave", LeaveAccrual.Yearly, 14, null, 0, false, true, null, 2, 1, null);
        var joinedJuly = new LeaveBalances(calendar, sick, D("2026-07-01"), [], []);
        Assert.Equal(0, joinedJuly.For(2026, D("2026-08-15")).Earned);   // under two months' service
        Assert.Equal(7, joinedJuly.For(2026, D("2026-09-01")).Earned);   // then 6/12 of 14 for the rest of the year
        Assert.Equal(14, joinedJuly.For(2027, D("2027-01-01")).Earned);  // a full year after
        Assert.False(joinedJuly.Eligible(D("2026-08-31")));
    }

    [Fact]
    public void Unused_days_carry_forward_up_to_the_cap_and_requests_count_down_the_balance()
    {
        var calendar = new LeaveCalendar(WorkWeek.MondayToFriday, []);
        var type = Annual(carry: 5);
        LeaveRequest Taken(string from, string to, decimal days, bool approve = true)
        {
            var r = new LeaveRequest(Guid.NewGuid(), type.Id, D(from), D(to), false, days, null, null, "x", DateTimeOffset.UtcNow);
            if (approve) r.Approve("boss", DateTimeOffset.UtcNow, null);
            return r;
        }
        var requests = new List<LeaveRequest> { Taken("2025-03-03", "2025-03-07", 5), Taken("2026-02-02", "2026-02-03", 2), Taken("2026-03-02", "2026-03-02", 1, approve: false) };
        var adjustments = new List<LeaveAdjustment> { new(Guid.NewGuid(), type.Id, 2026, 3, "Opening balance", "hr", DateTimeOffset.UtcNow) };
        var balances = new LeaveBalances(calendar, type, D("2024-01-01"), requests, adjustments);

        // 2024: 21 unused, 5 carry. 2025: 21 + 5 − 5 = 21 unused, 5 carry.
        var y2026 = balances.For(2026, D("2026-03-31"));
        Assert.Equal(new LeaveBalance(2026, 5.25m - 0.25m, 21, 5, 3, 2, 1, 5 + 5 + 3 - 2 - 1), y2026);
    }

    [Fact]
    public void A_request_spanning_two_leave_years_uses_its_stored_days_split_between_them()
    {
        var calendar = new LeaveCalendar(WorkWeek.MondayToFriday, [D("2027-01-01")]);
        var type = Annual(carry: 0);
        var r = new LeaveRequest(Guid.NewGuid(), type.Id, D("2026-12-28"), D("2027-01-05"), false, 6, null, null, "x", DateTimeOffset.UtcNow);
        r.Approve("boss", DateTimeOffset.UtcNow, null);
        var balances = new LeaveBalances(calendar, type, D("2020-01-01"), [r], []);
        Assert.Equal(4, balances.For(2026, D("2026-12-31")).Taken); // 28–31 Dec
        Assert.Equal(2, balances.For(2027, D("2027-01-31")).Taken); // 4–5 Jan (1 Jan is a holiday)
    }
}
