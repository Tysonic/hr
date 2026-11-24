namespace TyHr.Application.Organization;

public record BranchDto(Guid Id, string Name, string? Code, string? Address, bool IsActive, int EmployeeCount);
public record SaveBranchRequest(string Name, string? Code, string? Address);

public record DepartmentDto(Guid Id, string Name, string? Code, Guid? ParentId, string? ParentName, Guid? HeadEmployeeId,
    string? HeadName, bool IsActive, int EmployeeCount);
public record SaveDepartmentRequest(string Name, string? Code, Guid? ParentId, Guid? HeadEmployeeId);

public record JobGradeDto(Guid Id, string Code, string Name, int Rank, decimal? MinSalary, decimal? MaxSalary, bool IsActive, int PositionCount);
public record SaveJobGradeRequest(string Code, string Name, int Rank, decimal? MinSalary, decimal? MaxSalary);

public record PositionDto(Guid Id, string Title, Guid DepartmentId, string DepartmentName, Guid? JobGradeId, string? JobGradeCode,
    bool IsActive, int EmployeeCount);
public record SavePositionRequest(string Title, Guid DepartmentId, Guid? JobGradeId);

/// <summary>One active employee on the org chart; the web app builds the tree from <see cref="ManagerId"/>.</summary>
public record OrgChartNode(Guid EmployeeId, string EmployeeNumber, string Name, string? PositionTitle, string? DepartmentName, Guid? ManagerId);
