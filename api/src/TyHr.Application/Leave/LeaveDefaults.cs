using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain.Countries;
using TyHr.Domain.Leave;

namespace TyHr.Application.Leave;

/// <summary>A new company's starting leave set-up: its country's statutory leave types and this year's and next year's holidays.</summary>
public static class LeaveDefaults
{
    /// <summary>Adds what's missing (types by code, holidays by date); call with the company's tenant set.</summary>
    public static async Task SeedAsync(IAppDbContext db, CountryPack country, int year, CancellationToken ct)
    {
        var codes = (await db.LeaveTypes.Select(t => t.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in country.Calendar.LeaveTypes.Where(d => !codes.Contains(d.Code)))
            db.LeaveTypes.Add(new LeaveType(d.Code, d.Name, d.Accrual, d.DaysPerYear, d.MaxDaysPerRequest, d.CarryForwardMax,
                d.CountsCalendarDays, d.IsPaid, d.EligibleGender, d.MinServiceMonths, d.DocumentRequiredAfterDays, d.Description));
        var dates = (await db.PublicHolidays.Where(h => h.Date.Year >= year && h.Date.Year <= year + 1).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
        foreach (var h in country.Calendar.Holidays(year).Concat(country.Calendar.Holidays(year + 1)).Where(h => !dates.Contains(h.Date)))
            db.PublicHolidays.Add(new PublicHoliday(h.Date, h.Name));
        await db.SaveChangesAsync(ct);
    }
}
