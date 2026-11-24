using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Leave;

namespace TyHr.Application.Leave;

/// <summary>Leave types, public holidays and the working week: the company's leave set-up, seeded from its country pack.</summary>
public class LeaveSetupService(IAppDbContext db, ITenantContext tenantContext, TimeProvider clock)
{
    public async Task<IReadOnlyList<LeaveTypeDto>> ListTypesAsync(CancellationToken ct) =>
        (await db.LeaveTypes.AsNoTracking().OrderByDescending(t => t.IsActive).ThenBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<LeaveTypeDto> CreateTypeAsync(SaveLeaveTypeRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var type = new LeaveType(r.Code, r.Name, r.Accrual, r.DaysPerYear, r.MaxDaysPerRequest, r.CarryForwardMax, r.CountsCalendarDays,
            r.IsPaid, r.EligibleGender, r.MinServiceMonths, r.DocumentRequiredAfterDays, r.Description);
        await CheckUniqueAsync(type, ct);
        db.LeaveTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return ToDto(type);
    }

    public async Task<LeaveTypeDto> UpdateTypeAsync(Guid id, SaveLeaveTypeRequest r, CancellationToken ct)
    {
        var type = await FindTypeAsync(id, ct);
        if (type.Accrual != r.Accrual && await db.LeaveRequests.AnyAsync(x => x.LeaveTypeId == id, ct))
            throw new DomainException($"{type.Name} has requests, so how it is earned can't change; add a new type instead.");
        type.Update(r.Code, r.Name, r.Accrual, r.DaysPerYear, r.MaxDaysPerRequest, r.CarryForwardMax, r.CountsCalendarDays, r.IsPaid,
            r.EligibleGender, r.MinServiceMonths, r.DocumentRequiredAfterDays, r.Description);
        await CheckUniqueAsync(type, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(type);
    }

    public async Task<LeaveTypeDto> SetTypeActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var type = await FindTypeAsync(id, ct);
        type.SetActive(active);
        await db.SaveChangesAsync(ct);
        return ToDto(type);
    }

    /// <summary>Adds the country's statutory leave types the company doesn't have yet (matched by code).</summary>
    public async Task<IReadOnlyList<LeaveTypeDto>> AddCountryDefaultsAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var existing = (await db.LeaveTypes.Select(t => t.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in country.Calendar.LeaveTypes.Where(d => !existing.Contains(d.Code)))
            db.LeaveTypes.Add(new LeaveType(d.Code, d.Name, d.Accrual, d.DaysPerYear, d.MaxDaysPerRequest, d.CarryForwardMax,
                d.CountsCalendarDays, d.IsPaid, d.EligibleGender, d.MinServiceMonths, d.DocumentRequiredAfterDays, d.Description));
        await db.SaveChangesAsync(ct);
        return await ListTypesAsync(ct);
    }

    // Holidays

    public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(int year, CancellationToken ct) =>
        await db.PublicHolidays.AsNoTracking().Where(h => h.Date.Year == year).OrderBy(h => h.Date)
            .Select(h => new HolidayDto(h.Id, h.Date, h.Name)).ToListAsync(ct);

    public async Task<HolidayDto> AddHolidayAsync(SaveHolidayRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (await db.PublicHolidays.AnyAsync(h => h.Date == r.Date, ct)) throw new DomainException($"{r.Date:d MMM yyyy} is already a holiday.");
        var holiday = new PublicHoliday(r.Date, r.Name);
        db.PublicHolidays.Add(holiday);
        await db.SaveChangesAsync(ct);
        return new HolidayDto(holiday.Id, holiday.Date, holiday.Name);
    }

    public async Task DeleteHolidayAsync(Guid id, CancellationToken ct)
    {
        var holiday = await db.PublicHolidays.SingleOrDefaultAsync(h => h.Id == id, ct) ?? throw new NotFoundException("There is no such holiday.");
        db.PublicHolidays.Remove(holiday);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds the country's holidays for a year that aren't already on the company's list (by date).</summary>
    public async Task<GenerateHolidaysResult> GenerateHolidaysAsync(int year, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (year < 2000 || year > 2100) throw new DomainException("Choose a year between 2000 and 2100.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var taken = (await db.PublicHolidays.Where(h => h.Date.Year == year).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
        var proposed = country.Calendar.Holidays(year);
        var added = 0;
        foreach (var h in proposed.Where(h => !taken.Contains(h.Date)))
        {
            db.PublicHolidays.Add(new PublicHoliday(h.Date, h.Name));
            added++;
        }
        await db.SaveChangesAsync(ct);
        return new GenerateHolidaysResult(added, proposed.Count - added, country.Calendar.MovableHolidaysNote);
    }

    // Working week and leave year

    public async Task<LeaveSettingsDto> SettingsAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var profile = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var calendar = new LeaveCalendar(profile.WorkWeek, [], profile.FinancialYearStartMonth);
        return new LeaveSettingsDto(profile.WorkWeek.Days, calendar.YearStartMonth,
            calendar.LeaveYearOf(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)));
    }

    public async Task<LeaveSettingsDto> SaveWorkWeekAsync(SaveWorkWeekRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var profile = await db.CompanyProfiles.SingleAsync(ct);
        profile.SetWorkWeek(WorkWeek.Of(r.WorkingDays));
        await db.SaveChangesAsync(ct);
        return await SettingsAsync(ct);
    }

    private async Task CheckUniqueAsync(LeaveType type, CancellationToken ct)
    {
        if (await db.LeaveTypes.AnyAsync(t => t.Id != type.Id && t.Code == type.Code, ct))
            throw new DomainException($"There is already a leave type with the code {type.Code}.");
        if (await db.LeaveTypes.AnyAsync(t => t.Id != type.Id && t.Name.ToLower() == type.Name.ToLower(), ct))
            throw new DomainException($"There is already a leave type called {type.Name}.");
    }

    private async Task<LeaveType> FindTypeAsync(Guid id, CancellationToken ct) =>
        await db.LeaveTypes.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("There is no such leave type.");

    internal static LeaveTypeDto ToDto(LeaveType t) => new(t.Id, t.Code, t.Name, t.Accrual, t.DaysPerYear, t.MaxDaysPerRequest,
        t.CarryForwardMax, t.CountsCalendarDays, t.IsPaid, t.EligibleGender, t.MinServiceMonths, t.DocumentRequiredAfterDays, t.Description, t.IsActive);
}
