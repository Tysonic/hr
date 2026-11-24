using TyHr.Domain.Employees;
using TyHr.Domain.Payroll;

namespace TyHr.Application.Payroll;

public record PayComponentDto(Guid Id, string Code, string Name, PayComponentKind Kind, bool Taxable, bool Pensionable, string? Description, bool IsActive);
public record SavePayComponentRequest(string Code, string Name, PayComponentKind Kind, bool Taxable, bool Pensionable, string? Description);

public record PayItemDto(Guid Id, Guid ComponentId, string ComponentCode, string ComponentName, PayComponentKind Kind, decimal Amount,
    DateOnly StartDate, DateOnly? EndDate, string? Note, bool Current);
public record SavePayItemRequest(Guid ComponentId, decimal Amount, DateOnly StartDate, DateOnly? EndDate, string? Note);
public record SaveTaxProfileRequest(bool Resident, bool SecondaryEmployment);

/// <summary>An employee's pay set-up: salary today, recurring items and how they're taxed.</summary>
public record EmployeePayDto(Guid EmployeeId, string EmployeeName, string CurrencyCode, decimal? BasicSalary, DateOnly? SalaryEffectiveDate,
    bool Resident, bool SecondaryEmployment, IReadOnlyList<PayItemDto> Items);

public record PayrollSettingsDto(string CountryCode, string CountryName, string CurrencyCode, int CurrencyMinorUnits, DateOnly SupportedFrom,
    IReadOnlyList<string> Notes);

public record PayrollRunDto(Guid Id, int Year, int Month, DateOnly PeriodStart, DateOnly PeriodEnd, PayrollRunKind Kind, string Name, DateOnly PayDate,
    PayrollStatus Status, Guid? ReversesRunId, Guid? ReversedByRunId, string CreatedBy, DateTimeOffset CreatedAt, string? CalculatedBy,
    DateTimeOffset? CalculatedAt, string? SubmittedBy, DateTimeOffset? SubmittedAt, string? ApprovedBy, DateTimeOffset? ApprovedAt,
    string? LockedBy, DateTimeOffset? LockedAt, string? ReturnNote, int EmployeeCount, decimal Gross, decimal Deductions, decimal Net,
    decimal EmployerCost, IReadOnlyList<string> Warnings, string CurrencyCode, bool CanPrepare, bool CanApprove, bool CanReverse);

public record CreatePayrollRunRequest(int Year, int Month, PayrollRunKind Kind, string? Name, DateOnly? PayDate);
public record UpdatePayrollRunRequest(string Name, DateOnly PayDate);
public record SendBackRequest(string Note);

public record PayrollInputDto(Guid Id, Guid EmployeeId, string EmployeeNumber, string EmployeeName, Guid ComponentId, string ComponentName,
    PayComponentKind Kind, decimal Amount, string? Note);
public record AddPayrollInputRequest(Guid EmployeeId, Guid ComponentId, decimal Amount, string? Note);

public record PayslipSummaryDto(Guid Id, Guid EmployeeId, string EmployeeNumber, string EmployeeName, string? DepartmentName, decimal Gross,
    decimal Paye, decimal TotalDeductions, decimal Net, decimal EmployerContributions, IReadOnlyList<string> Warnings);

/// <summary>A payslip in full. Account numbers show only their last digits.</summary>
public record PayslipDto(Guid Id, Guid RunId, string RunName, int Year, int Month, DateOnly PayDate, PayrollStatus RunStatus, Guid EmployeeId,
    string EmployeeNumber, string EmployeeName, string? DepartmentName, string? PositionTitle, string? TaxNumber, string? SocialSecurityNumber,
    PaymentMethod PaymentMethod, string? PaidTo, IReadOnlyList<PayLine> Lines, decimal Gross, decimal TotalDeductions, decimal Net,
    decimal EmployerContributions, IReadOnlyList<string> Warnings, string CurrencyCode);

/// <summary>How someone's pay differs from the previous month's locked payrolls.</summary>
public record VarianceRow(Guid EmployeeId, string EmployeeNumber, string EmployeeName, string Change, decimal? PreviousGross, decimal? Gross,
    decimal? PreviousNet, decimal? Net, decimal NetChange, decimal? NetChangePercent);

public record PayrollVarianceDto(int PreviousYear, int PreviousMonth, decimal PreviousGross, decimal PreviousNet, decimal Gross, decimal Net,
    IReadOnlyList<VarianceRow> Rows);
