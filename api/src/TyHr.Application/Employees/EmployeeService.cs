using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;

namespace TyHr.Application.Employees;

/// <summary>
/// Employee records: the people, their contracts, salary history and next of kin. Reads go through
/// <see cref="EmployeeAccess"/> (whose records, which parts); the API's policies decide who may change what.
/// </summary>
public class EmployeeService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IDocumentNumbers numbers,
    EmployeeAccess access,
    ICurrentUser user,
    TimeProvider clock)
{
    public const string NumberPrefix = "EMP";

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    // Lists

    public async Task<PagedResult<EmployeeListItem>> ListAsync(EmployeeQuery q, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(q.Page, q.PageSize);
        var query = (await access.VisibleAsync(ct)).AsNoTracking();
        if (Guard.Optional(q.Search) is { } search) query = query.Where(Matches(search));
        if (q.DepartmentId is { } departmentId) query = query.Where(e => e.DepartmentId == departmentId);
        if (q.BranchId is { } branchId) query = query.Where(e => e.BranchId == branchId);
        if (q.Status is { } status) query = query.Where(e => e.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ThenBy(e => e.EmployeeNumber)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new EmployeeListItem(e.Id, e.EmployeeNumber,
                e.FirstName + " " + (e.MiddleName == null ? "" : e.MiddleName + " ") + e.LastName,
                db.Positions.Where(p => p.Id == e.PositionId).Select(p => p.Title).FirstOrDefault(),
                db.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                db.Branches.Where(b => b.Id == e.BranchId).Select(b => b.Name).FirstOrDefault(),
                db.Employees.Where(m => m.Id == e.ManagerId).Select(m => m.FirstName + " " + m.LastName).FirstOrDefault(),
                e.WorkEmail, e.HireDate, e.Status))
            .ToListAsync(ct);
        return new PagedResult<EmployeeListItem>(items, page, pageSize, total);
    }

    /// <summary>Active and suspended colleagues, for anyone in the company: work details only.</summary>
    public async Task<PagedResult<DirectoryEntry>> DirectoryAsync(string? search, Guid? departmentId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var query = db.Employees.AsNoTracking().Where(e => e.Status != EmployeeStatus.Exited);
        if (Guard.Optional(search) is { } s) query = query.Where(Matches(s));
        if (departmentId is { } d) query = query.Where(e => e.DepartmentId == d);

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new DirectoryEntry(e.Id, e.FirstName + " " + e.LastName,
                db.Positions.Where(p => p.Id == e.PositionId).Select(p => p.Title).FirstOrDefault(),
                db.Departments.Where(x => x.Id == e.DepartmentId).Select(x => x.Name).FirstOrDefault(),
                db.Branches.Where(b => b.Id == e.BranchId).Select(b => b.Name).FirstOrDefault(),
                e.WorkEmail, e.WorkPhone,
                db.Employees.Where(m => m.Id == e.ManagerId).Select(m => m.FirstName + " " + m.LastName).FirstOrDefault()))
            .ToListAsync(ct);
        return new PagedResult<DirectoryEntry>(items, page, pageSize, total);
    }

    /// <summary>Name, number or work email containing the text (any case). ID numbers are encrypted, so not searchable.</summary>
    private static System.Linq.Expressions.Expression<Func<Employee, bool>> Matches(string search)
    {
        var term = search.ToLowerInvariant();
        return e => (e.FirstName + " " + e.LastName).ToLower().Contains(term)
                    || (e.LastName + " " + e.FirstName).ToLower().Contains(term)
                    || (e.MiddleName != null && e.MiddleName.ToLower().Contains(term))
                    || e.EmployeeNumber.ToLower().Contains(term)
                    || (e.WorkEmail != null && e.WorkEmail.Contains(term));
    }

    // One employee

    public async Task<EmployeeDto> GetAsync(Guid id, CancellationToken ct)
    {
        if (!await access.CanOpenAsync(id, ct)) throw new NotFoundException("There is no such employee.");
        return await BuildAsync(id, access.CanSeePersonal, access.CanSeePay, access.CanEdit, access.CanSeePay, ct);
    }

    /// <summary>The signed-in employee's own record, with their personal and pay details, read-only.</summary>
    public async Task<EmployeeDto> MineAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct) ?? throw new DomainException(NotLinked);
        return await BuildAsync(me, personal: true, pay: true, edit: false, editPay: false, ct);
    }

    public const string NotLinked = "Your sign-in isn't linked to an employee record. Ask HR to set your work email on your record.";

    private async Task<EmployeeDto> BuildAsync(Guid id, bool personal, bool pay, bool edit, bool editPay, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("There is no such employee.");

        var position = e.PositionId == null ? null
            : await db.Positions.AsNoTracking().Where(p => p.Id == e.PositionId).Select(p => new { p.Title, p.JobGradeId }).SingleAsync(ct);
        var grade = position?.JobGradeId == null ? null
            : await db.JobGrades.AsNoTracking().SingleAsync(g => g.Id == position.JobGradeId, ct);
        var employment = new EmploymentDto(e.HireDate, e.WorkEmail, e.WorkPhone,
            e.BranchId, await db.Branches.Where(b => b.Id == e.BranchId).Select(b => b.Name).SingleOrDefaultAsync(ct),
            e.DepartmentId, await db.Departments.Where(d => d.Id == e.DepartmentId).Select(d => d.Name).SingleOrDefaultAsync(ct),
            e.PositionId, position?.Title, grade?.Code,
            e.ManagerId, await db.Employees.Where(m => m.Id == e.ManagerId).Select(m => m.FirstName + " " + m.LastName).SingleOrDefaultAsync(ct));

        var today = Today;
        var contracts = (await db.EmploymentContracts.AsNoTracking().Where(c => c.EmployeeId == id)
                .OrderByDescending(c => c.StartDate).ToListAsync(ct))
            .Select(c => new ContractDto(c.Id, c.Type, c.StartDate, c.EndDate, c.ProbationEndDate, c.ConfirmedOn, c.Notes, c.IsCurrentOn(today)))
            .ToList();

        PersonalDto? personalDto = null;
        IReadOnlyList<NextOfKinDto>? nextOfKin = null;
        if (personal)
        {
            personalDto = new PersonalDto(e.Gender, e.DateOfBirth, e.MaritalStatus, e.Nationality, e.IdType, e.IdNumber, e.TaxNumber,
                e.SocialSecurityNumber, e.PersonalEmail, e.PersonalPhone, e.HomeAddress);
            nextOfKin = await db.NextOfKin.AsNoTracking().Where(k => k.EmployeeId == id).OrderBy(k => k.Name)
                .Select(k => new NextOfKinDto(k.Id, k.Name, k.Relationship, k.Phone, k.Email, k.Address)).ToListAsync(ct);
        }

        PaymentDto? payment = null;
        IReadOnlyList<SalaryDto>? salaries = null;
        if (pay)
        {
            payment = new PaymentDto(e.PaymentMethod, e.BankName, e.BankBranch, e.BankAccountNumber, e.BankAccountName,
                e.MobileMoneyProvider, e.MobileMoneyNumber);
            var history = await db.SalaryChanges.AsNoTracking().Where(s => s.EmployeeId == id)
                .OrderByDescending(s => s.EffectiveDate).ToListAsync(ct);
            var current = history.FirstOrDefault(s => s.EffectiveDate <= today);
            salaries = history.Select(s => new SalaryDto(s.Id, s.EffectiveDate, s.MonthlyBasic, s.CurrencyCode, s.Reason,
                s == current, grade != null && (s.MonthlyBasic < grade.MinSalary || s.MonthlyBasic > grade.MaxSalary),
                s.RecordedBy, s.RecordedAt)).ToList();
        }

        return new EmployeeDto(e.Id, e.EmployeeNumber, e.FullName, e.FirstName, e.MiddleName, e.LastName, e.Status, employment,
            personalDto, payment, contracts, salaries, nextOfKin,
            new EmployeeAccessDto(edit, personal, pay, editPay));
    }

    // Changes

    /// <summary>Adds an employee with the next EMP number, and optionally their first contract and starting salary.</summary>
    public async Task<EmployeeDto> CreateAsync(CreateEmployeeRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (r.Salary != null && !access.CanSeePay) throw new DomainException("You can't set salaries.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);

        var id = await db.InTransactionAsync(async () =>
        {
            var employee = new Employee(await numbers.NextAsync(NumberPrefix, ct), r.Personal, country, r.Employment);
            await CheckEmploymentAsync(employee, r.Employment, ct);
            await CheckWorkEmailAsync(employee, ct);
            db.Employees.Add(employee);
            if (r.Contract is { } c)
                db.EmploymentContracts.Add(new EmploymentContract(employee.Id, c.Type, c.StartDate, c.EndDate, c.ProbationEndDate, c.Notes));
            if (r.Salary is { } s)
                db.SalaryChanges.Add(new SalaryChange(employee.Id, s.EffectiveDate, s.MonthlyBasic, country, s.Reason, user.UserName, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
            return employee.Id;
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<EmployeeDto> UpdatePersonalAsync(Guid id, PersonalDetails personal, CancellationToken ct)
    {
        var employee = await FindAsync(id, ct);
        employee.UpdatePersonal(personal, await db.CompanyCountryAsync(tenantContext, ct));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<EmployeeDto> UpdateEmploymentAsync(Guid id, EmploymentDetails employment, CancellationToken ct)
    {
        var employee = await FindAsync(id, ct);
        employee.UpdateEmployment(employment);
        await CheckEmploymentAsync(employee, employment, ct);
        await CheckWorkEmailAsync(employee, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<EmployeeDto> UpdatePaymentAsync(Guid id, PaymentDetails payment, CancellationToken ct)
    {
        var employee = await FindAsync(id, ct);
        employee.UpdatePayment(payment, await db.CompanyCountryAsync(tenantContext, ct));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Suspends or reactivates. Exits (with notice, clearance and final dues) come with exit management.</summary>
    public async Task<EmployeeDto> SetStatusAsync(Guid id, EmployeeStatus status, CancellationToken ct)
    {
        if (status == EmployeeStatus.Exited) throw new DomainException("Exits go through exit management, not a status change.");
        var employee = await FindAsync(id, ct);
        if (employee.Status == EmployeeStatus.Exited) throw new DomainException($"{employee.FullName} has left the company.");
        employee.SetStatus(status);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // Contracts

    public async Task<EmployeeDto> AddContractAsync(Guid employeeId, SaveContractRequest r, CancellationToken ct)
    {
        await FindAsync(employeeId, ct);
        var contract = new EmploymentContract(employeeId, r.Type, r.StartDate, r.EndDate, r.ProbationEndDate, r.Notes);
        await CheckNoOverlapAsync(contract, ct);
        db.EmploymentContracts.Add(contract);
        await db.SaveChangesAsync(ct);
        return await GetAsync(employeeId, ct);
    }

    public async Task<EmployeeDto> UpdateContractAsync(Guid employeeId, Guid contractId, SaveContractRequest r, CancellationToken ct)
    {
        var contract = await FindContractAsync(employeeId, contractId, ct);
        contract.Update(r.Type, r.StartDate, r.EndDate, r.ProbationEndDate, r.Notes);
        await CheckNoOverlapAsync(contract, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(employeeId, ct);
    }

    public async Task<EmployeeDto> ConfirmContractAsync(Guid employeeId, Guid contractId, DateOnly confirmedOn, CancellationToken ct)
    {
        var contract = await FindContractAsync(employeeId, contractId, ct);
        contract.Confirm(confirmedOn);
        await db.SaveChangesAsync(ct);
        return await GetAsync(employeeId, ct);
    }

    private async Task CheckNoOverlapAsync(EmploymentContract c, CancellationToken ct)
    {
        var end = c.EndDate ?? DateOnly.MaxValue;
        var others = await db.EmploymentContracts.AsNoTracking()
            .Where(o => o.EmployeeId == c.EmployeeId && o.Id != c.Id).ToListAsync(ct);
        if (others.Any(o => o.StartDate <= end && (o.EndDate ?? DateOnly.MaxValue) >= c.StartDate))
            throw new DomainException("This contract overlaps another of the employee's contracts. End the other one first.");
    }

    private async Task<EmploymentContract> FindContractAsync(Guid employeeId, Guid contractId, CancellationToken ct) =>
        await db.EmploymentContracts.SingleOrDefaultAsync(c => c.Id == contractId && c.EmployeeId == employeeId, ct)
        ?? throw new NotFoundException("There is no such contract.");

    // Salary

    public async Task<EmployeeDto> AddSalaryAsync(Guid employeeId, AddSalaryRequest r, CancellationToken ct)
    {
        var employee = await FindAsync(employeeId, ct);
        if (r.EffectiveDate < employee.HireDate) throw new DomainException("A salary can't take effect before the hire date.");
        if (await db.SalaryChanges.AnyAsync(s => s.EmployeeId == employeeId && s.EffectiveDate == r.EffectiveDate, ct))
            throw new DomainException($"There is already a salary change effective {r.EffectiveDate:yyyy-MM-dd}.");
        db.SalaryChanges.Add(new SalaryChange(employeeId, r.EffectiveDate, r.MonthlyBasic, await db.CompanyCountryAsync(tenantContext, ct),
            r.Reason, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return await GetAsync(employeeId, ct);
    }

    /// <summary>Only a change that hasn't taken effect yet can be removed (to correct it); past ones are history.</summary>
    public async Task<EmployeeDto> DeleteSalaryAsync(Guid employeeId, Guid salaryId, CancellationToken ct)
    {
        var change = await db.SalaryChanges.SingleOrDefaultAsync(s => s.Id == salaryId && s.EmployeeId == employeeId, ct)
            ?? throw new NotFoundException("There is no such salary change.");
        if (change.EffectiveDate <= Today)
            throw new DomainException("This salary has already taken effect, so it stays in the history. Add a new change instead.");
        db.SalaryChanges.Remove(change);
        await db.SaveChangesAsync(ct);
        return await GetAsync(employeeId, ct);
    }

    // Next of kin

    public async Task<EmployeeDto> AddNextOfKinAsync(Guid employeeId, SaveNextOfKinRequest r, CancellationToken ct)
    {
        await FindAsync(employeeId, ct);
        db.NextOfKin.Add(new NextOfKin(employeeId, r.Name, r.Relationship, r.Phone, r.Email, r.Address, await db.CompanyCountryAsync(tenantContext, ct)));
        await db.SaveChangesAsync(ct);
        return await GetAsync(employeeId, ct);
    }

    public async Task<EmployeeDto> UpdateNextOfKinAsync(Guid employeeId, Guid kinId, SaveNextOfKinRequest r, CancellationToken ct)
    {
        var kin = await FindKinAsync(employeeId, kinId, ct);
        kin.Update(r.Name, r.Relationship, r.Phone, r.Email, r.Address, await db.CompanyCountryAsync(tenantContext, ct));
        await db.SaveChangesAsync(ct);
        return await GetAsync(employeeId, ct);
    }

    public async Task<EmployeeDto> DeleteNextOfKinAsync(Guid employeeId, Guid kinId, CancellationToken ct)
    {
        db.NextOfKin.Remove(await FindKinAsync(employeeId, kinId, ct));
        await db.SaveChangesAsync(ct);
        return await GetAsync(employeeId, ct);
    }

    private async Task<NextOfKin> FindKinAsync(Guid employeeId, Guid kinId, CancellationToken ct) =>
        await db.NextOfKin.SingleOrDefaultAsync(k => k.Id == kinId && k.EmployeeId == employeeId, ct)
        ?? throw new NotFoundException("There is no such next of kin.");

    // Checks

    private async Task<Employee> FindAsync(Guid id, CancellationToken ct) =>
        await db.Employees.SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("There is no such employee.");

    /// <summary>Branch, department and position are active; the position is in the department; the manager chain has no loop.</summary>
    private async Task CheckEmploymentAsync(Employee employee, EmploymentDetails e, CancellationToken ct)
    {
        if (e.BranchId is { } branchId && !await db.Branches.AnyAsync(b => b.Id == branchId && b.IsActive, ct))
            throw new DomainException("Choose an active branch.");
        if (e.DepartmentId is { } departmentId && !await db.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive, ct))
            throw new DomainException("Choose an active department.");
        if (e.PositionId is { } positionId)
        {
            var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == positionId && p.IsActive, ct)
                ?? throw new DomainException("Choose an active position.");
            if (e.DepartmentId != null && position.DepartmentId != e.DepartmentId)
                throw new DomainException($"The position {position.Title} belongs to another department.");
        }
        if (e.ManagerId is { } managerId)
        {
            var managers = await db.Employees.AsNoTracking().Where(x => x.Status != EmployeeStatus.Exited)
                .ToDictionaryAsync(x => x.Id, x => x.ManagerId, ct);
            if (!managers.ContainsKey(managerId)) throw new DomainException("The manager must be a current employee.");
            for (Guid? at = managerId; at != null; at = managers.GetValueOrDefault(at.Value))
                if (at == employee.Id) throw new DomainException("That manager reports to this employee, which would make a loop.");
        }
    }

    private async Task CheckWorkEmailAsync(Employee employee, CancellationToken ct)
    {
        if (employee.WorkEmail is { } email && await db.Employees.AnyAsync(x => x.Id != employee.Id && x.WorkEmail == email, ct))
            throw new DomainException($"Another employee already has the work email {email}.");
    }
}
