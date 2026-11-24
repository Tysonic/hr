namespace TyHr.Domain.Organization;

/// <summary>
/// A place the company works from (head office, a branch, a site). Employees are based at one; public holidays and
/// work schedules can differ by location in later milestones.
/// </summary>
public class Branch : TenantEntity
{
    private Branch() { }

    public Branch(string name, string? code, string? address)
    {
        Update(name, code, address);
        IsActive = true;
    }

    public string Name { get; private set; } = null!;
    public string? Code { get; private set; }
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string? code, string? address)
    {
        Name = Guard.Required(name, "Branch name");
        Code = Guard.Optional(code)?.ToUpperInvariant();
        Address = Guard.Optional(address);
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>
/// A unit of the organisation. Departments nest (Finance → Accounts Payable) through <see cref="ParentId"/>; the
/// service keeps the tree free of cycles, since that needs the other departments.
/// </summary>
public class Department : TenantEntity
{
    private Department() { }

    public Department(string name, string? code, Guid? parentId)
    {
        Update(name, code, parentId);
        IsActive = true;
    }

    public string Name { get; private set; } = null!;
    public string? Code { get; private set; }
    public Guid? ParentId { get; private set; }

    /// <summary>The employee heading the department, if set. Their team's leave can route to them.</summary>
    public Guid? HeadEmployeeId { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string? code, Guid? parentId)
    {
        if (parentId == Id) throw new DomainException("A department can't be its own parent.");
        Name = Guard.Required(name, "Department name");
        Code = Guard.Optional(code)?.ToUpperInvariant();
        ParentId = parentId;
    }

    public void SetHead(Guid? employeeId) => HeadEmployeeId = employeeId;

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>
/// A pay and seniority level (G1, Manager, ...). <see cref="Rank"/> orders them; the optional salary range is a
/// guide that salary changes outside it are flagged against.
/// </summary>
public class JobGrade : TenantEntity
{
    private JobGrade() { }

    public JobGrade(string code, string name, int rank, decimal? minSalary, decimal? maxSalary)
    {
        Update(code, name, rank, minSalary, maxSalary);
        IsActive = true;
    }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int Rank { get; private set; }
    public decimal? MinSalary { get; private set; }
    public decimal? MaxSalary { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string code, string name, int rank, decimal? minSalary, decimal? maxSalary)
    {
        if (minSalary < 0 || maxSalary < 0) throw new DomainException("Salary range amounts can't be negative.");
        if (minSalary > maxSalary) throw new DomainException("The grade's minimum salary is above its maximum.");
        Code = Guard.Required(code, "Grade code").ToUpperInvariant();
        Name = Guard.Required(name, "Grade name");
        Rank = rank;
        MinSalary = minSalary;
        MaxSalary = maxSalary;
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>A job in a department (Accountant in Finance), at a grade. Employees hold one position.</summary>
public class Position : TenantEntity
{
    private Position() { }

    public Position(string title, Guid departmentId, Guid? jobGradeId)
    {
        Update(title, departmentId, jobGradeId);
        IsActive = true;
    }

    public string Title { get; private set; } = null!;
    public Guid DepartmentId { get; private set; }
    public Guid? JobGradeId { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string title, Guid departmentId, Guid? jobGradeId)
    {
        if (departmentId == Guid.Empty) throw new DomainException("A position needs a department.");
        Title = Guard.Required(title, "Position title");
        DepartmentId = departmentId;
        JobGradeId = jobGradeId;
    }

    public void SetActive(bool active) => IsActive = active;
}
