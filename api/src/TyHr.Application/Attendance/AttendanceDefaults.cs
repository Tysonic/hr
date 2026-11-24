using TyHr.Application.Common;
using TyHr.Domain.Attendance;
using TyHr.Domain.Leave;

namespace TyHr.Application.Attendance;

/// <summary>A new company's starting schedule: office hours, the default for everyone until HR sets up others.</summary>
public static class AttendanceDefaults
{
    public static async Task SeedAsync(IAppDbContext db, CancellationToken ct)
    {
        var schedule = new WorkSchedule("Office hours", new TimeOnly(8, 0), new TimeOnly(17, 0), 60, WorkWeek.MondayToFriday, 15, 30);
        schedule.SetDefault(true);
        db.WorkSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);
    }
}
