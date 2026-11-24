using TyHr.Domain.Countries;

namespace TyHr.Domain.Payroll;

public enum PayComponentKind { Earning, Deduction }

/// <summary>
/// Something paid or taken off besides basic salary and statutory items: allowances, overtime and bonuses (earnings);
/// loans, advances, SACCO and union dues (deductions, taken after tax). Earnings say whether they're taxed and whether
/// they count for NSSF.
/// </summary>
public class PayComponent : TenantEntity
{
    private PayComponent() { }

    public PayComponent(string code, string name, PayComponentKind kind, bool taxable, bool pensionable, string? description = null)
    {
        Kind = kind;
        Update(code, name, taxable, pensionable, description);
        IsActive = true;
    }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public PayComponentKind Kind { get; private set; }
    public bool Taxable { get; private set; }
    public bool Pensionable { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string code, string name, bool taxable, bool pensionable, string? description)
    {
        Code = Guard.Required(code, "Code").ToUpperInvariant();
        if (Code.Length > 20 || !Code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new DomainException("The code can have up to 20 letters, digits and underscores.");
        if (Code is "BASIC" or "PAYE" or "NSSF" or "NSSF_ER" or "LST" or "SHIF" or "AHL" or "AHL_ER" or "NITA" or "UNPAID")
            throw new DomainException($"{Code} is used by TyHR for salary and statutory items; choose another code.");
        Name = Guard.Required(name, "Name");
        Taxable = Kind == PayComponentKind.Earning && taxable;
        Pensionable = Kind == PayComponentKind.Earning && pensionable;
        Description = Guard.Optional(description);
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>A component paid or deducted every month for an employee, from a start month until an optional end.</summary>
public class EmployeePayItem : TenantEntity
{
    private EmployeePayItem() { }

    public EmployeePayItem(Guid employeeId, Guid componentId, decimal amount, DateOnly startDate, DateOnly? endDate, string? note, CountryPack country)
    {
        EmployeeId = employeeId;
        ComponentId = componentId;
        Update(amount, startDate, endDate, note, country);
    }

    public Guid EmployeeId { get; private set; }
    public Guid ComponentId { get; private set; }

    /// <summary>The monthly amount.</summary>
    public decimal Amount { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string? Note { get; private set; }

    public bool AppliesIn(DateOnly periodStart, DateOnly periodEnd) => StartDate <= periodEnd && (EndDate == null || EndDate >= periodStart);

    public void Update(decimal amount, DateOnly startDate, DateOnly? endDate, string? note, CountryPack country)
    {
        Payroll.Amounts.Check(amount, country, allowZero: false);
        if (endDate < startDate) throw new DomainException("It ends before it starts.");
        Amount = amount;
        StartDate = startDate;
        EndDate = endDate;
        Note = Guard.Optional(note);
    }

    public void End(DateOnly on)
    {
        if (on < StartDate) throw new DomainException("It can't end before it starts.");
        EndDate = on;
    }
}

/// <summary>How an employee is taxed. Without one, they are a resident in their primary employment.</summary>
public class EmployeeTaxProfile : TenantEntity
{
    private EmployeeTaxProfile() { }

    public EmployeeTaxProfile(Guid employeeId, bool resident, bool secondaryEmployment)
    {
        EmployeeId = employeeId;
        Update(resident, secondaryEmployment);
    }

    public Guid EmployeeId { get; private set; }
    public bool Resident { get; private set; }

    /// <summary>This isn't the employee's main job (another employer gives them their tax-free band or relief).</summary>
    public bool SecondaryEmployment { get; private set; }

    public void Update(bool resident, bool secondaryEmployment)
    {
        Resident = resident;
        SecondaryEmployment = secondaryEmployment;
    }
}

public static class Amounts
{
    public static void Check(decimal amount, CountryPack country, bool allowZero = true, bool allowNegative = false)
    {
        if (!allowNegative && amount < 0) throw new DomainException("The amount can't be negative.");
        if (!allowZero && amount == 0) throw new DomainException("Enter an amount.");
        if (!country.IsValidAmount(amount)) throw new DomainException($"{country.CurrencyCode} amounts have {country.CurrencyMinorUnits} decimal places.");
    }
}
