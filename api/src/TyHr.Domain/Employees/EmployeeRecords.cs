using TyHr.Domain.Countries;

namespace TyHr.Domain.Employees;

/// <summary>Someone to contact about an employee in an emergency (and, in some countries, a beneficiary).</summary>
public class NextOfKin : TenantEntity
{
    private NextOfKin() { }

    public NextOfKin(Guid employeeId, string name, string relationship, string phone, string? email, string? address, CountryPack country)
    {
        EmployeeId = employeeId;
        Update(name, relationship, phone, email, address, country);
    }

    public Guid EmployeeId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Relationship { get; private set; } = null!;
    public string Phone { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? Address { get; private set; }

    public void Update(string name, string relationship, string phone, string? email, string? address, CountryPack country)
    {
        Name = Guard.Required(name, "Next of kin name");
        Relationship = Guard.Required(relationship, "Relationship");
        Phone = country.Formats.NormalizePhone(Guard.Required(phone, "Next of kin phone"), country.PhonePrefix);
        Email = Guard.Optional(email)?.ToLowerInvariant();
        Address = Guard.Optional(address);
    }
}

public enum ContractType { Permanent, FixedTerm, Casual, Internship }

/// <summary>
/// The terms an employee works under for a period. A new contract (renewal, change of terms) is added rather than the
/// old one edited, so the history stays. Fixed-term contracts and internships have an end date; any contract can start
/// with a probation period. Contract and probation ends feed the expiry reminders.
/// </summary>
public class EmploymentContract : TenantEntity
{
    private EmploymentContract() { }

    public EmploymentContract(Guid employeeId, ContractType type, DateOnly startDate, DateOnly? endDate, DateOnly? probationEndDate, string? notes)
    {
        EmployeeId = employeeId;
        Update(type, startDate, endDate, probationEndDate, notes);
    }

    public Guid EmployeeId { get; private set; }
    public ContractType Type { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public DateOnly? ProbationEndDate { get; private set; }

    /// <summary>When the employee was confirmed after probation (null while on probation or without one).</summary>
    public DateOnly? ConfirmedOn { get; private set; }

    public string? Notes { get; private set; }

    public bool IsCurrentOn(DateOnly day) => StartDate <= day && (EndDate == null || EndDate >= day);

    public void Update(ContractType type, DateOnly startDate, DateOnly? endDate, DateOnly? probationEndDate, string? notes)
    {
        if (type is ContractType.FixedTerm or ContractType.Internship && endDate == null)
            throw new DomainException("A fixed-term contract or internship needs an end date.");
        if (endDate < startDate) throw new DomainException("The contract ends before it starts.");
        if (probationEndDate != null && (probationEndDate < startDate || probationEndDate > endDate))
            throw new DomainException("Probation must end within the contract.");
        Type = type;
        StartDate = startDate;
        EndDate = endDate;
        ProbationEndDate = probationEndDate;
        Notes = Guard.Optional(notes);
    }

    public void Confirm(DateOnly on)
    {
        if (ProbationEndDate == null) throw new DomainException("This contract has no probation to confirm.");
        if (ConfirmedOn != null) throw new DomainException("The employee is already confirmed.");
        if (on < StartDate) throw new DomainException("Confirmation can't be before the contract starts.");
        ConfirmedOn = on;
    }

    /// <summary>Ends the contract early when the employee leaves; a probation still running ends with it.</summary>
    public void EndOn(DateOnly lastDay)
    {
        if (lastDay < StartDate) throw new DomainException("The contract can't end before it starts.");
        if (EndDate != null && EndDate <= lastDay) return;
        EndDate = lastDay;
        if (ProbationEndDate > lastDay) ProbationEndDate = lastDay;
    }

    /// <summary>Moves the end of an unconfirmed probation later, within the contract.</summary>
    public void ExtendProbation(DateOnly to)
    {
        if (ProbationEndDate == null) throw new DomainException("This contract has no probation to extend.");
        if (ConfirmedOn != null) throw new DomainException("The employee is already confirmed.");
        if (to <= ProbationEndDate) throw new DomainException("The new probation end must be after the current one.");
        if (to > EndDate) throw new DomainException("Probation must end within the contract.");
        ProbationEndDate = to;
    }
}

/// <summary>
/// The employee's basic monthly salary from <see cref="EffectiveDate"/> until the next change (effective-dated, so
/// payroll for any month finds the salary that applied then). Changes are added, never edited, except to correct one
/// that hasn't taken effect yet.
/// </summary>
public class SalaryChange : TenantEntity
{
    private SalaryChange() { }

    public SalaryChange(Guid employeeId, DateOnly effectiveDate, decimal monthlyBasic, CountryPack country, string reason,
        string recordedBy, DateTimeOffset recordedAt)
    {
        if (monthlyBasic <= 0) throw new DomainException("The salary must be more than zero.");
        if (!country.IsValidAmount(monthlyBasic))
            throw new DomainException($"{country.CurrencyCode} amounts have {country.CurrencyMinorUnits} decimal places.");
        EmployeeId = employeeId;
        EffectiveDate = effectiveDate;
        MonthlyBasic = monthlyBasic;
        CurrencyCode = country.CurrencyCode;
        Reason = Guard.Required(reason, "Reason");
        RecordedBy = recordedBy;
        RecordedAt = recordedAt;
    }

    public Guid EmployeeId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public decimal MonthlyBasic { get; private set; }
    public string CurrencyCode { get; private set; } = null!;

    /// <summary>Starting salary, promotion, annual review...</summary>
    public string Reason { get; private set; } = null!;

    public string RecordedBy { get; private set; } = null!;
    public DateTimeOffset RecordedAt { get; private set; }
}
