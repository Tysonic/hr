using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain.Employees;

namespace TyHr.Application.Employees;

/// <summary>
/// Which employee records the signed-in user may open, and which parts of them. HR and payroll see everyone; a line
/// manager sees the people under them in the reporting line (directly or further down), found from the employee record
/// whose work email is their sign-in email; anyone else sees no records (only the directory).
/// </summary>
public class EmployeeAccess(IAppDbContext db, ICurrentUser user)
{
    private HashSet<Guid>? _team;
    private Guid? _me;
    private bool _meLoaded;

    public bool SeesEveryone => user.Can(Permissions.EmployeesViewAll);
    public bool CanSeePersonal => user.Can(Permissions.EmployeesPersonal);
    public bool CanSeePay => user.Can(Permissions.EmployeesPay);
    public bool CanEdit => user.Can(Permissions.EmployeesManage);

    /// <summary>The employee records the user may open.</summary>
    public async Task<IQueryable<Employee>> VisibleAsync(CancellationToken ct)
    {
        if (SeesEveryone) return db.Employees;
        if (!user.Can(Permissions.EmployeesView)) return db.Employees.Where(_ => false);
        var team = await TeamAsync(ct);
        return db.Employees.Where(e => team.Contains(e.Id));
    }

    public async Task<bool> CanOpenAsync(Guid employeeId, CancellationToken ct) =>
        SeesEveryone || (user.Can(Permissions.EmployeesView) && (await TeamAsync(ct)).Contains(employeeId));

    /// <summary>The user's own employee record: the current employee whose work email is their sign-in email.</summary>
    public async Task<Guid?> MeAsync(CancellationToken ct)
    {
        if (_meLoaded) return _me;
        _meLoaded = true;
        if (!string.IsNullOrEmpty(user.Email))
            _me = await db.Employees.AsNoTracking().Where(e => e.WorkEmail == user.Email && e.Status != EmployeeStatus.Exited)
                .Select(e => (Guid?)e.Id).SingleOrDefaultAsync(ct);
        return _me;
    }

    /// <summary>Whether the employee is below the user in the reporting line (directly or further down).</summary>
    public async Task<bool> InTeamAsync(Guid employeeId, CancellationToken ct) => (await TeamAsync(ct)).Contains(employeeId);

    /// <summary>Everyone below the user's own employee record in the reporting line (not the user).</summary>
    public async Task<HashSet<Guid>> TeamAsync(CancellationToken ct)
    {
        if (_team != null) return _team;
        _team = [];
        var me = await MeAsync(ct);
        if (me == null) return _team;

        var reports = (await db.Employees.AsNoTracking().Where(e => e.ManagerId != null)
                .Select(e => new { e.Id, e.ManagerId }).ToListAsync(ct))
            .ToLookup(e => e.ManagerId!.Value, e => e.Id);
        var queue = new Queue<Guid>([me.Value]);
        while (queue.TryDequeue(out var manager))
            foreach (var report in reports[manager])
                if (_team.Add(report)) queue.Enqueue(report);
        return _team;
    }
}
