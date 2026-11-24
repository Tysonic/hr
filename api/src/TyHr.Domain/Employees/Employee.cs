using TyHr.Domain.Auditing;
using TyHr.Domain.Countries;

namespace TyHr.Domain.Employees;

public enum Gender { Female, Male }

public enum MaritalStatus { Single, Married, Divorced, Widowed }

/// <summary>Which document <see cref="Employee.IdNumber"/> is from. Only national IDs are checked against the country's format.</summary>
public enum IdType { NationalId, Passport, RefugeeId, Other }

public enum EmployeeStatus { Active, Suspended, Exited }

public enum PaymentMethod { Bank, MobileMoney, Cash }

/// <summary>
/// A person employed by the company: who they are, where they sit in the organisation, and how they're paid.
/// Contracts, salary history and next of kin are separate records (<see cref="EmploymentContract"/>,
/// <see cref="SalaryChange"/>, <see cref="NextOfKin"/>). ID and account numbers are stored encrypted.
/// </summary>
public class Employee : TenantEntity
{
    private Employee() { }

    public Employee(string employeeNumber, PersonalDetails personal, CountryPack country, EmploymentDetails employment)
    {
        EmployeeNumber = Guard.Required(employeeNumber, "Employee number");
        UpdatePersonal(personal, country);
        UpdateEmployment(employment);
        Status = EmployeeStatus.Active;
        PaymentMethod = PaymentMethod.Bank;
    }

    /// <summary>EMP-0001, from the company's document numbers. Never reused.</summary>
    public string EmployeeNumber { get; private set; } = null!;

    // Personal
    public string FirstName { get; private set; } = null!;
    public string? MiddleName { get; private set; }
    public string LastName { get; private set; } = null!;
    public Gender Gender { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public MaritalStatus? MaritalStatus { get; private set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? Nationality { get; private set; }

    public IdType? IdType { get; private set; }

    [Encrypted]
    public string? IdNumber { get; private set; }

    /// <summary>Taxpayer number (TIN, KRA PIN), checked against the company's country format.</summary>
    public string? TaxNumber { get; private set; }

    public string? SocialSecurityNumber { get; private set; }
    public string? PersonalEmail { get; private set; }
    public string? PersonalPhone { get; private set; }
    public string? HomeAddress { get; private set; }

    // Employment
    /// <summary>Also how the employee is matched to their sign-in (the token's email) for self-service and team access.</summary>
    public string? WorkEmail { get; private set; }

    public string? WorkPhone { get; private set; }
    public DateOnly HireDate { get; private set; }
    public Guid? BranchId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public Guid? PositionId { get; private set; }

    /// <summary>The employee's line manager (reporting line). The service keeps the chain free of cycles.</summary>
    public Guid? ManagerId { get; private set; }

    public EmployeeStatus Status { get; private set; }

    // Payment
    public PaymentMethod PaymentMethod { get; private set; }
    public string? BankName { get; private set; }
    public string? BankBranch { get; private set; }

    [Encrypted]
    public string? BankAccountNumber { get; private set; }

    public string? BankAccountName { get; private set; }

    /// <summary>MTN MoMo, Airtel Money, M-Pesa...</summary>
    public string? MobileMoneyProvider { get; private set; }

    [Encrypted]
    public string? MobileMoneyNumber { get; private set; }

    public string FullName => string.Join(' ', new[] { FirstName, MiddleName, LastName }.Where(n => !string.IsNullOrEmpty(n)));

    public void UpdatePersonal(PersonalDetails p, CountryPack country)
    {
        if (p.DateOfBirth is { } dob && dob.Year < 1900) throw new DomainException("Date of birth is not valid.");
        FirstName = Guard.Required(p.FirstName, "First name");
        MiddleName = Guard.Optional(p.MiddleName);
        LastName = Guard.Required(p.LastName, "Last name");
        Gender = p.Gender;
        DateOfBirth = p.DateOfBirth;
        MaritalStatus = p.MaritalStatus;
        Nationality = Guard.Optional(p.Nationality)?.ToUpperInvariant();
        if (Nationality is { Length: not 2 }) throw new DomainException("Nationality must be a 2-letter country code, e.g. UG.");

        var idNumber = Guard.Optional(p.IdNumber);
        if (idNumber != null && p.IdType == null) throw new DomainException("Say which kind of ID the ID number is from.");
        IdType = idNumber == null ? null : p.IdType;
        IdNumber = idNumber == null ? null
            : p.IdType == Employees.IdType.NationalId ? country.Formats.NormalizeNationalId(idNumber) : idNumber.ToUpperInvariant();

        TaxNumber = Guard.Optional(p.TaxNumber) is { } tax ? country.Formats.NormalizeTaxNumber(tax) : null;
        SocialSecurityNumber = Guard.Optional(p.SocialSecurityNumber)?.ToUpperInvariant();
        PersonalEmail = Email(p.PersonalEmail);
        PersonalPhone = Guard.Optional(p.PersonalPhone) is { } phone ? country.Formats.NormalizePhone(phone, country.PhonePrefix) : null;
        HomeAddress = Guard.Optional(p.HomeAddress);
    }

    /// <summary>Sets where the employee sits. The caller has checked the ids belong to the company and the manager chain.</summary>
    public void UpdateEmployment(EmploymentDetails e)
    {
        if (e.ManagerId != null && e.ManagerId == Id) throw new DomainException("An employee can't be their own manager.");
        WorkEmail = Email(e.WorkEmail);
        WorkPhone = Guard.Optional(e.WorkPhone);
        HireDate = e.HireDate;
        BranchId = e.BranchId;
        DepartmentId = e.DepartmentId;
        PositionId = e.PositionId;
        ManagerId = e.ManagerId;
    }

    public void UpdatePayment(PaymentDetails p, CountryPack country)
    {
        PaymentMethod = p.Method;
        BankName = BankBranch = BankAccountNumber = BankAccountName = MobileMoneyProvider = MobileMoneyNumber = null;
        switch (p.Method)
        {
            case PaymentMethod.Bank:
                BankName = Guard.Required(p.BankName, "Bank name");
                BankBranch = Guard.Optional(p.BankBranch);
                BankAccountNumber = Guard.Required(p.BankAccountNumber, "Bank account number").Replace(" ", "");
                BankAccountName = Guard.Optional(p.BankAccountName);
                break;
            case PaymentMethod.MobileMoney:
                MobileMoneyProvider = Guard.Required(p.MobileMoneyProvider, "Mobile money provider");
                MobileMoneyNumber = country.Formats.NormalizePhone(Guard.Required(p.MobileMoneyNumber, "Mobile money number"), country.PhonePrefix);
                break;
        }
    }

    public void SetStatus(EmployeeStatus status) => Status = status;

    /// <summary>When personal data was removed under the retention policy; the record stays for history under its number.</summary>
    public DateTimeOffset? AnonymisedAt { get; private set; }

    /// <summary>Removes who the person was: names, contacts, identifiers, payment details and birth date. Gender and employment history stay.</summary>
    public void Anonymise(DateTimeOffset at)
    {
        if (Status != EmployeeStatus.Exited) throw new DomainException("Only former employees' personal data can be removed.");
        if (AnonymisedAt != null) throw new DomainException("This person's personal data has already been removed.");
        FirstName = "Former employee";
        MiddleName = null;
        LastName = EmployeeNumber;
        (DateOfBirth, MaritalStatus, Nationality, IdType, IdNumber, TaxNumber, SocialSecurityNumber) = (null, null, null, null, null, null, null);
        (PersonalEmail, PersonalPhone, HomeAddress, WorkEmail, WorkPhone) = (null, null, null, null, null);
        (BankName, BankBranch, BankAccountNumber, BankAccountName, MobileMoneyProvider, MobileMoneyNumber) = (null, null, null, null, null, null);
        ManagerId = null;
        AnonymisedAt = at;
    }

    private static string? Email(string? value)
    {
        var email = Guard.Optional(value)?.ToLowerInvariant();
        if (email != null && (!email.Contains('@') || email.Contains(' ') || email.Length > 254))
            throw new DomainException($"'{value}' is not a valid email address.");
        return email;
    }
}

public record PersonalDetails(
    string FirstName,
    string? MiddleName,
    string LastName,
    Gender Gender,
    DateOnly? DateOfBirth,
    MaritalStatus? MaritalStatus,
    string? Nationality,
    IdType? IdType,
    string? IdNumber,
    string? TaxNumber,
    string? SocialSecurityNumber,
    string? PersonalEmail,
    string? PersonalPhone,
    string? HomeAddress);

public record EmploymentDetails(
    DateOnly HireDate,
    string? WorkEmail,
    string? WorkPhone,
    Guid? BranchId,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? ManagerId);

public record PaymentDetails(
    PaymentMethod Method,
    string? BankName,
    string? BankBranch,
    string? BankAccountNumber,
    string? BankAccountName,
    string? MobileMoneyProvider,
    string? MobileMoneyNumber);
