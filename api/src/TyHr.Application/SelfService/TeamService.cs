using Microsoft.EntityFrameworkCore;
using TyHr.Application.Attendance;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Leave;
using TyHr.Domain.Employees;
using TyHr.Domain.Leave;
using TyHr.Domain.Payroll;

namespace TyHr.Application.SelfService;

/// <summary>A manager's view of the people below them, and what waits for anyone to decide.</summary>
public class TeamService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, EmployeeAccess access, AttendanceService attendance,
    LeaveService leave, ProfileChangeService changes, Onboarding.ProbationService probation, Exits.ExitService exits, TimeProvider clock)
{
    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);

    /// <summary>
    /// Everyone below the user in the reporting line, whatever their role: today's attendance and leave in the next 30
    /// days. Empty when nobody reports to them.
    /// </summary>
    public async Task<IReadOnlyList<TeamMemberDto>> MyTeamAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct);
        var team = await access.TeamAsync(ct);
        if (me == null || team.Count == 0) return [];
        var people = await db.Employees.AsNoTracking().Where(e => team.Contains(e.Id) && e.Status != EmployeeStatus.Exited)
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync(ct);
        var today = await TodayAsync(ct);
        var days = user.Can(Permissions.AttendanceView)
            ? (await attendance.DayAsync(today, null, ct)).ToDictionary(d => d.EmployeeId, d => d.Day)
            : [];
        var positions = await db.Positions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var horizon = today.AddDays(30);
        var ids = people.Select(p => p.Id).ToList();
        var upcoming = (await db.LeaveRequests.AsNoTracking()
                .Where(r => ids.Contains(r.EmployeeId) && (r.Status == LeaveStatus.Approved || r.Status == LeaveStatus.Pending)
                            && r.EndDate >= today && r.StartDate <= horizon)
                .OrderBy(r => r.StartDate).ToListAsync(ct))
            .ToLookup(r => r.EmployeeId);
        return people.Select(p => new TeamMemberDto(p.Id, p.EmployeeNumber, p.FullName,
            p.PositionId is { } pos ? positions.GetValueOrDefault(pos) : null, p.DepartmentId is { } d ? departments.GetValueOrDefault(d) : null,
            p.ManagerId == me, p.Status, days.GetValueOrDefault(p.Id),
            upcoming[p.Id].Select(r => new TeamLeave(r.StartDate, r.EndDate, types.GetValueOrDefault(r.LeaveTypeId, "Leave"), r.Status == LeaveStatus.Pending)).ToList()))
            .ToList();
    }

    public async Task<InboxDto> InboxAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var leaveCount = user.Can(Permissions.LeaveApprove) ? (await leave.ListAsync(new LeaveRequestQuery("approvals", PageSize: 1), ct)).TotalCount : 0;
        var changeCount = await changes.PendingCountAsync(ct);
        var payroll = 0;
        if (user.Can(Permissions.PayrollApprove))
            payroll = await db.PayrollRuns.CountAsync(r => r.Status == PayrollStatus.Submitted && r.CalculatedBy != user.UserName && r.SubmittedBy != user.UserName, ct);
        var requisitions = user.Can(Permissions.RecruitmentApprove)
            ? await db.JobRequisitions.CountAsync(r => r.Status == Domain.Recruitment.RequisitionStatus.Pending && r.RequestedBy != user.UserName, ct)
            : 0;
        var probations = user.Can(Permissions.EmployeesView) ? await probation.WaitingCountAsync(ct) : 0;
        var resignations = await exits.WaitingCountAsync(ct);
        return new InboxDto(leaveCount, changeCount, payroll, requisitions, probations, resignations,
            leaveCount + changeCount + payroll + requisitions + probations + resignations);
    }
}
