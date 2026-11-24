using TyHr.Domain.Employees;

namespace TyHr.Application.Employees;

public record EmployeeListItem(Guid Id, string EmployeeNumber, string FullName, string? PositionTitle, string? DepartmentName,
    string? BranchName, string? ManagerName, string? WorkEmail, DateOnly HireDate, EmployeeStatus Status);

public record EmployeeQuery(string? Search = null, Guid? DepartmentId = null, Guid? BranchId = null, EmployeeStatus? Status = null,
    int Page = 1, int PageSize = 50);

/// <summary>
/// One employee as the signed-in user may see them. <see cref="Personal"/> is null without the personal-data
/// permission; <see cref="Payment"/> and <see cref="Salaries"/> are null without the pay permission.
/// </summary>
public record EmployeeDto(
    Guid Id,
    string EmployeeNumber,
    string FullName,
    string FirstName,
    string? MiddleName,
    string LastName,
    EmployeeStatus Status,
    EmploymentDto Employment,
    PersonalDto? Personal,
    PaymentDto? Payment,
    IReadOnlyList<ContractDto> Contracts,
    IReadOnlyList<SalaryDto>? Salaries,
    IReadOnlyList<NextOfKinDto>? NextOfKin,
    EmployeeAccessDto Access);

public record EmploymentDto(DateOnly HireDate, string? WorkEmail, string? WorkPhone, Guid? BranchId, string? BranchName,
    Guid? DepartmentId, string? DepartmentName, Guid? PositionId, string? PositionTitle, string? JobGradeCode,
    Guid? ManagerId, string? ManagerName);

public record PersonalDto(Gender Gender, DateOnly? DateOfBirth, MaritalStatus? MaritalStatus, string? Nationality, IdType? IdType,
    string? IdNumber, string? TaxNumber, string? SocialSecurityNumber, string? PersonalEmail, string? PersonalPhone, string? HomeAddress);

public record PaymentDto(PaymentMethod Method, string? BankName, string? BankBranch, string? BankAccountNumber, string? BankAccountName,
    string? MobileMoneyProvider, string? MobileMoneyNumber);

public record ContractDto(Guid Id, ContractType Type, DateOnly StartDate, DateOnly? EndDate, DateOnly? ProbationEndDate,
    DateOnly? ConfirmedOn, string? Notes, bool IsCurrent);

/// <summary><see cref="OutsideGrade"/>: the amount is outside the employee's job grade range (a warning, not an error).</summary>
public record SalaryDto(Guid Id, DateOnly EffectiveDate, decimal MonthlyBasic, string CurrencyCode, string Reason, bool IsCurrent,
    bool OutsideGrade, string RecordedBy, DateTimeOffset RecordedAt);

public record NextOfKinDto(Guid Id, string Name, string Relationship, string Phone, string? Email, string? Address);

/// <summary>What the signed-in user may do with this record, so the web app shows only those actions.</summary>
public record EmployeeAccessDto(bool CanEdit, bool CanSeePersonal, bool CanSeePay, bool CanEditPay);

public record CreateEmployeeRequest(
    PersonalDetails Personal,
    EmploymentDetails Employment,
    SaveContractRequest? Contract,
    AddSalaryRequest? Salary);

public record SaveContractRequest(ContractType Type, DateOnly StartDate, DateOnly? EndDate, DateOnly? ProbationEndDate, string? Notes);

public record ConfirmContractRequest(DateOnly ConfirmedOn);

public record AddSalaryRequest(DateOnly EffectiveDate, decimal MonthlyBasic, string Reason);

public record SaveNextOfKinRequest(string Name, string Relationship, string Phone, string? Email, string? Address);

public record SetStatusRequest(EmployeeStatus Status);

/// <summary>The staff directory: what any colleague may see.</summary>
public record DirectoryEntry(Guid Id, string FullName, string? PositionTitle, string? DepartmentName, string? BranchName,
    string? WorkEmail, string? WorkPhone, string? ManagerName);
