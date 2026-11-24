using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Organization;

namespace TyHr.Application.Organization;

/// <summary>
/// The company's structure: branches, departments (a tree), job grades and positions. Things in use can't be deleted,
/// only deactivated, so history keeps pointing at them; inactive ones can't be given to employees or new positions.
/// Every query is limited to the request's company by the tenant filter.
/// </summary>
public class OrgService(IAppDbContext db, ITenantContext tenantContext)
{
    // Branches

    public async Task<IReadOnlyList<BranchDto>> ListBranchesAsync(CancellationToken ct) =>
        await db.Branches.AsNoTracking().OrderBy(b => b.Name)
            .Select(b => new BranchDto(b.Id, b.Name, b.Code, b.Address, b.IsActive,
                db.Employees.Count(e => e.BranchId == b.Id && e.Status != EmployeeStatus.Exited)))
            .ToListAsync(ct);

    public async Task<BranchDto> CreateBranchAsync(SaveBranchRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var branch = new Branch(r.Name, r.Code, r.Address);
        await EnsureUniqueAsync(db.Branches, b => b.Name == branch.Name, $"There is already a branch called {branch.Name}.", ct);
        if (branch.Code != null) await EnsureUniqueAsync(db.Branches, b => b.Code == branch.Code, $"There is already a branch with code {branch.Code}.", ct);
        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);
        return await GetBranchAsync(branch.Id, ct);
    }

    public async Task<BranchDto> UpdateBranchAsync(Guid id, SaveBranchRequest r, CancellationToken ct)
    {
        var branch = await FindAsync(db.Branches, id, "branch", ct);
        branch.Update(r.Name, r.Code, r.Address);
        await EnsureUniqueAsync(db.Branches, b => b.Id != id && b.Name == branch.Name, $"There is already a branch called {branch.Name}.", ct);
        if (branch.Code != null) await EnsureUniqueAsync(db.Branches, b => b.Id != id && b.Code == branch.Code, $"There is already a branch with code {branch.Code}.", ct);
        await db.SaveChangesAsync(ct);
        return await GetBranchAsync(id, ct);
    }

    public async Task<BranchDto> SetBranchActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var branch = await FindAsync(db.Branches, id, "branch", ct);
        if (!active && await db.Employees.AnyAsync(e => e.BranchId == id && e.Status != EmployeeStatus.Exited, ct))
            throw new DomainException($"{branch.Name} still has employees. Move them to another branch first.");
        branch.SetActive(active);
        await db.SaveChangesAsync(ct);
        return await GetBranchAsync(id, ct);
    }

    public async Task DeleteBranchAsync(Guid id, CancellationToken ct)
    {
        var branch = await FindAsync(db.Branches, id, "branch", ct);
        if (await db.Employees.AnyAsync(e => e.BranchId == id, ct))
            throw new DomainException($"{branch.Name} is used by employee records, so it can't be deleted. Deactivate it instead.");
        db.Branches.Remove(branch);
        await db.SaveChangesAsync(ct);
    }

    private async Task<BranchDto> GetBranchAsync(Guid id, CancellationToken ct) =>
        (await ListBranchesAsync(ct)).Single(b => b.Id == id);

    // Departments

    public async Task<IReadOnlyList<DepartmentDto>> ListDepartmentsAsync(CancellationToken ct) =>
        await db.Departments.AsNoTracking().OrderBy(d => d.Name)
            .Select(d => new DepartmentDto(d.Id, d.Name, d.Code, d.ParentId,
                db.Departments.Where(p => p.Id == d.ParentId).Select(p => p.Name).FirstOrDefault(),
                d.HeadEmployeeId,
                db.Employees.Where(e => e.Id == d.HeadEmployeeId).Select(e => e.FirstName + " " + e.LastName).FirstOrDefault(),
                d.IsActive,
                db.Employees.Count(e => e.DepartmentId == d.Id && e.Status != EmployeeStatus.Exited)))
            .ToListAsync(ct);

    public async Task<DepartmentDto> CreateDepartmentAsync(SaveDepartmentRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var department = new Department(r.Name, r.Code, r.ParentId);
        await CheckDepartmentAsync(department, r, ct);
        db.Departments.Add(department);
        await db.SaveChangesAsync(ct);
        return await GetDepartmentAsync(department.Id, ct);
    }

    public async Task<DepartmentDto> UpdateDepartmentAsync(Guid id, SaveDepartmentRequest r, CancellationToken ct)
    {
        var department = await FindAsync(db.Departments, id, "department", ct);
        department.Update(r.Name, r.Code, r.ParentId);
        await CheckDepartmentAsync(department, r, ct);
        await db.SaveChangesAsync(ct);
        return await GetDepartmentAsync(id, ct);
    }

    private async Task CheckDepartmentAsync(Department department, SaveDepartmentRequest r, CancellationToken ct)
    {
        var id = department.Id;
        await EnsureUniqueAsync(db.Departments, d => d.Id != id && d.Name == department.Name, $"There is already a department called {department.Name}.", ct);
        if (department.Code != null)
            await EnsureUniqueAsync(db.Departments, d => d.Id != id && d.Code == department.Code, $"There is already a department with code {department.Code}.", ct);

        if (r.ParentId is { } parentId)
        {
            // Walk up from the new parent: reaching this department again would make a loop.
            var parents = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => (d.ParentId, d.IsActive), ct);
            if (!parents.TryGetValue(parentId, out var parent)) throw new DomainException("The parent department doesn't exist.");
            if (!parent.IsActive) throw new DomainException("The parent department is inactive.");
            for (Guid? at = parentId; at != null; at = parents.GetValueOrDefault(at.Value).ParentId)
                if (at == id) throw new DomainException("A department can't sit under one of its own sub-departments.");
        }

        if (r.HeadEmployeeId is { } headId && !await db.Employees.AnyAsync(e => e.Id == headId && e.Status == EmployeeStatus.Active, ct))
            throw new DomainException("The department head must be an active employee.");
        department.SetHead(r.HeadEmployeeId);
    }

    public async Task<DepartmentDto> SetDepartmentActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var department = await FindAsync(db.Departments, id, "department", ct);
        if (!active)
        {
            if (await db.Employees.AnyAsync(e => e.DepartmentId == id && e.Status != EmployeeStatus.Exited, ct))
                throw new DomainException($"{department.Name} still has employees. Move them first.");
            if (await db.Departments.AnyAsync(d => d.ParentId == id && d.IsActive, ct))
                throw new DomainException($"{department.Name} has active sub-departments. Move or deactivate them first.");
        }
        department.SetActive(active);
        await db.SaveChangesAsync(ct);
        return await GetDepartmentAsync(id, ct);
    }

    public async Task DeleteDepartmentAsync(Guid id, CancellationToken ct)
    {
        var department = await FindAsync(db.Departments, id, "department", ct);
        if (await db.Employees.AnyAsync(e => e.DepartmentId == id, ct) || await db.Positions.AnyAsync(p => p.DepartmentId == id, ct)
            || await db.Departments.AnyAsync(d => d.ParentId == id, ct))
            throw new DomainException($"{department.Name} is in use (employees, positions or sub-departments), so it can't be deleted. Deactivate it instead.");
        db.Departments.Remove(department);
        await db.SaveChangesAsync(ct);
    }

    private async Task<DepartmentDto> GetDepartmentAsync(Guid id, CancellationToken ct) =>
        (await ListDepartmentsAsync(ct)).Single(d => d.Id == id);

    // Job grades

    public async Task<IReadOnlyList<JobGradeDto>> ListJobGradesAsync(CancellationToken ct) =>
        await db.JobGrades.AsNoTracking().OrderBy(g => g.Rank).ThenBy(g => g.Code)
            .Select(g => new JobGradeDto(g.Id, g.Code, g.Name, g.Rank, g.MinSalary, g.MaxSalary, g.IsActive,
                db.Positions.Count(p => p.JobGradeId == g.Id)))
            .ToListAsync(ct);

    public async Task<JobGradeDto> CreateJobGradeAsync(SaveJobGradeRequest r, CancellationToken ct)
    {
        var grade = new JobGrade(r.Code, r.Name, r.Rank, r.MinSalary, r.MaxSalary);
        await CheckGradeAsync(grade, ct);
        db.JobGrades.Add(grade);
        await db.SaveChangesAsync(ct);
        return (await ListJobGradesAsync(ct)).Single(g => g.Id == grade.Id);
    }

    public async Task<JobGradeDto> UpdateJobGradeAsync(Guid id, SaveJobGradeRequest r, CancellationToken ct)
    {
        var grade = await FindAsync(db.JobGrades, id, "job grade", ct);
        grade.Update(r.Code, r.Name, r.Rank, r.MinSalary, r.MaxSalary);
        await CheckGradeAsync(grade, ct);
        await db.SaveChangesAsync(ct);
        return (await ListJobGradesAsync(ct)).Single(g => g.Id == id);
    }

    private async Task CheckGradeAsync(JobGrade grade, CancellationToken ct)
    {
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        foreach (var amount in new[] { grade.MinSalary, grade.MaxSalary })
            if (amount is { } a && !country.IsValidAmount(a))
                throw new DomainException($"{country.CurrencyCode} amounts have {country.CurrencyMinorUnits} decimal places.");
        await EnsureUniqueAsync(db.JobGrades, g => g.Id != grade.Id && g.Code == grade.Code, $"There is already a grade {grade.Code}.", ct);
    }

    public async Task<JobGradeDto> SetJobGradeActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var grade = await FindAsync(db.JobGrades, id, "job grade", ct);
        if (!active && await db.Positions.AnyAsync(p => p.JobGradeId == id && p.IsActive, ct))
            throw new DomainException($"Active positions are at grade {grade.Code}. Regrade or deactivate them first.");
        grade.SetActive(active);
        await db.SaveChangesAsync(ct);
        return (await ListJobGradesAsync(ct)).Single(g => g.Id == id);
    }

    public async Task DeleteJobGradeAsync(Guid id, CancellationToken ct)
    {
        var grade = await FindAsync(db.JobGrades, id, "job grade", ct);
        if (await db.Positions.AnyAsync(p => p.JobGradeId == id, ct))
            throw new DomainException($"Positions use grade {grade.Code}, so it can't be deleted. Deactivate it instead.");
        db.JobGrades.Remove(grade);
        await db.SaveChangesAsync(ct);
    }

    // Positions

    public async Task<IReadOnlyList<PositionDto>> ListPositionsAsync(CancellationToken ct) =>
        await db.Positions.AsNoTracking()
            .Join(db.Departments, p => p.DepartmentId, d => d.Id, (p, d) => new { p, d })
            .OrderBy(x => x.d.Name).ThenBy(x => x.p.Title)
            .Select(x => new PositionDto(x.p.Id, x.p.Title, x.p.DepartmentId, x.d.Name, x.p.JobGradeId,
                db.JobGrades.Where(g => g.Id == x.p.JobGradeId).Select(g => g.Code).FirstOrDefault(),
                x.p.IsActive,
                db.Employees.Count(e => e.PositionId == x.p.Id && e.Status != EmployeeStatus.Exited)))
            .ToListAsync(ct);

    public async Task<PositionDto> CreatePositionAsync(SavePositionRequest r, CancellationToken ct)
    {
        var position = new Position(r.Title, r.DepartmentId, r.JobGradeId);
        await CheckPositionAsync(position, ct);
        db.Positions.Add(position);
        await db.SaveChangesAsync(ct);
        return (await ListPositionsAsync(ct)).Single(p => p.Id == position.Id);
    }

    public async Task<PositionDto> UpdatePositionAsync(Guid id, SavePositionRequest r, CancellationToken ct)
    {
        var position = await FindAsync(db.Positions, id, "position", ct);
        position.Update(r.Title, r.DepartmentId, r.JobGradeId);
        await CheckPositionAsync(position, ct);
        await db.SaveChangesAsync(ct);
        return (await ListPositionsAsync(ct)).Single(p => p.Id == id);
    }

    private async Task CheckPositionAsync(Position position, CancellationToken ct)
    {
        if (!await db.Departments.AnyAsync(d => d.Id == position.DepartmentId && d.IsActive, ct))
            throw new DomainException("Choose an active department.");
        if (position.JobGradeId is { } gradeId && !await db.JobGrades.AnyAsync(g => g.Id == gradeId && g.IsActive, ct))
            throw new DomainException("Choose an active job grade.");
        await EnsureUniqueAsync(db.Positions, p => p.Id != position.Id && p.DepartmentId == position.DepartmentId && p.Title == position.Title,
            $"The department already has a position called {position.Title}.", ct);
    }

    public async Task<PositionDto> SetPositionActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var position = await FindAsync(db.Positions, id, "position", ct);
        if (!active && await db.Employees.AnyAsync(e => e.PositionId == id && e.Status != EmployeeStatus.Exited, ct))
            throw new DomainException($"Employees hold the position {position.Title}. Move them first.");
        position.SetActive(active);
        await db.SaveChangesAsync(ct);
        return (await ListPositionsAsync(ct)).Single(p => p.Id == id);
    }

    public async Task DeletePositionAsync(Guid id, CancellationToken ct)
    {
        var position = await FindAsync(db.Positions, id, "position", ct);
        if (await db.Employees.AnyAsync(e => e.PositionId == id, ct))
            throw new DomainException($"{position.Title} is used by employee records, so it can't be deleted. Deactivate it instead.");
        db.Positions.Remove(position);
        await db.SaveChangesAsync(ct);
    }

    // Org chart

    public async Task<IReadOnlyList<OrgChartNode>> OrgChartAsync(CancellationToken ct) =>
        await db.Employees.AsNoTracking()
            .Where(e => e.Status != EmployeeStatus.Exited)
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Select(e => new OrgChartNode(e.Id, e.EmployeeNumber, e.FirstName + " " + e.LastName,
                db.Positions.Where(p => p.Id == e.PositionId).Select(p => p.Title).FirstOrDefault(),
                db.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                e.ManagerId))
            .ToListAsync(ct);

    // Helpers

    private static async Task<T> FindAsync<T>(DbSet<T> set, Guid id, string what, CancellationToken ct) where T : TenantEntity =>
        await set.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException($"There is no such {what}.");

    private static async Task EnsureUniqueAsync<T>(IQueryable<T> set, System.Linq.Expressions.Expression<Func<T, bool>> clash,
        string message, CancellationToken ct)
    {
        if (await set.AnyAsync(clash, ct)) throw new DomainException(message);
    }
}
