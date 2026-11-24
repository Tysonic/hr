using TyHr.Domain.Auditing;
using TyHr.Domain.Employees;

namespace TyHr.Domain.Payroll;

public enum PayrollRunKind { Regular, OffCycle, Reversal }
public enum PayrollStatus { Draft, Calculated, Submitted, Approved, Locked }

/// <summary>
/// A payroll for one month. Regular runs pay everyone's salary; off-cycle runs pay one-off amounts (bonuses,
/// corrections) to chosen people; a reversal cancels a locked run line for line.
/// <para>Draft → Calculated → Submitted (for approval) → Approved → Locked. Changing inputs sends a run back to
/// Draft; the approver can send it back with a note. Whoever calculated or submitted a run can't approve it, and
/// nothing changes once it's approved except locking. <see cref="Version"/> stops two people changing it at once.</para>
/// </summary>
public class PayrollRun : TenantEntity
{
    private PayrollRun() { }

    public PayrollRun(int year, int month, PayrollRunKind kind, string name, DateOnly payDate, string createdBy, DateTimeOffset at, Guid? reversesRunId = null)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100) throw new DomainException("Choose a valid month.");
        Year = year;
        Month = month;
        Kind = kind;
        Name = Guard.Required(name, "Name");
        if (payDate < PeriodStart.AddDays(-15) || payDate > PeriodEnd.AddDays(45)) throw new DomainException("The pay date should be close to the month being paid.");
        PayDate = payDate;
        ReversesRunId = reversesRunId;
        Status = PayrollStatus.Draft;
        CreatedBy = createdBy;
        CreatedAt = at;
    }

    public int Year { get; private set; }
    public int Month { get; private set; }
    public DateOnly PeriodStart => new(Year, Month, 1);
    public DateOnly PeriodEnd => PeriodStart.AddMonths(1).AddDays(-1);
    public PayrollRunKind Kind { get; private set; }
    public string Name { get; private set; } = null!;
    public DateOnly PayDate { get; private set; }
    public Guid? ReversesRunId { get; private set; }
    public PayrollStatus Status { get; private set; }

    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public string? CalculatedBy { get; private set; }
    public DateTimeOffset? CalculatedAt { get; private set; }
    public string? SubmittedBy { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? LockedBy { get; private set; }
    public DateTimeOffset? LockedAt { get; private set; }

    /// <summary>Why the approver sent it back, until it's submitted again.</summary>
    public string? ReturnNote { get; private set; }

    public int EmployeeCount { get; private set; }
    public decimal Gross { get; private set; }
    public decimal Deductions { get; private set; }
    public decimal Net { get; private set; }
    public decimal EmployerCost { get; private set; }

    /// <summary>Problems found when calculating (people left out, missing details).</summary>
    public List<string> Warnings { get; private set; } = [];

    /// <summary>Postgres xmin: a concurrent change makes the second save fail instead of overwriting.</summary>
    public uint Version { get; private set; }

    public bool IsFinal => Status is PayrollStatus.Approved or PayrollStatus.Locked;

    public void Rename(string name, DateOnly payDate)
    {
        RequireEditable();
        Name = Guard.Required(name, "Name");
        PayDate = payDate;
    }

    public void InputsChanged()
    {
        RequireEditable();
        if (Status != PayrollStatus.Draft) Reset();
    }

    public void Calculated(IReadOnlyCollection<Payslip> payslips, IEnumerable<string> warnings, string by, DateTimeOffset at)
    {
        RequireEditable();
        EmployeeCount = payslips.Count;
        Gross = payslips.Sum(p => p.Gross);
        Deductions = payslips.Sum(p => p.TotalDeductions);
        Net = payslips.Sum(p => p.Net);
        EmployerCost = payslips.Sum(p => p.EmployerContributions);
        Warnings = warnings.ToList();
        Status = PayrollStatus.Calculated;
        CalculatedBy = by;
        CalculatedAt = at;
        SubmittedBy = ApprovedBy = null;
        SubmittedAt = ApprovedAt = null;
    }

    public void Submit(string by, DateTimeOffset at)
    {
        if (Status != PayrollStatus.Calculated) throw new DomainException(Status == PayrollStatus.Draft ? "Calculate the payroll first." : $"The payroll is already {Describe()}.");
        if (EmployeeCount == 0) throw new DomainException("Nobody is paid in this payroll.");
        Status = PayrollStatus.Submitted;
        SubmittedBy = by;
        SubmittedAt = at;
        ReturnNote = null;
    }

    public void Approve(string by, DateTimeOffset at)
    {
        if (Status != PayrollStatus.Submitted) throw new DomainException(Status == PayrollStatus.Approved || Status == PayrollStatus.Locked
            ? $"The payroll is already {Describe()}." : "The payroll has to be submitted for approval first.");
        if (string.Equals(by, CalculatedBy, StringComparison.OrdinalIgnoreCase) || string.Equals(by, SubmittedBy, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Someone other than the person who prepared this payroll has to approve it.");
        Status = PayrollStatus.Approved;
        ApprovedBy = by;
        ApprovedAt = at;
    }

    public void SendBack(string note)
    {
        if (Status is not (PayrollStatus.Submitted or PayrollStatus.Approved)) throw new DomainException("Only a payroll waiting for approval or approved can be sent back.");
        ReturnNote = Guard.Required(note, "Say what needs changing");
        Status = PayrollStatus.Calculated;
        SubmittedBy = ApprovedBy = null;
        SubmittedAt = ApprovedAt = null;
    }

    public void Lock(string by, DateTimeOffset at)
    {
        if (Status != PayrollStatus.Approved) throw new DomainException(Status == PayrollStatus.Locked ? "The payroll is already locked." : "Only an approved payroll can be locked.");
        Status = PayrollStatus.Locked;
        LockedBy = by;
        LockedAt = at;
    }

    public void RequireEditable()
    {
        if (IsFinal) throw new DomainException($"The payroll is {Describe()}, so it can't change. Reverse it if it was wrong.");
        if (Status == PayrollStatus.Submitted) throw new DomainException("The payroll is waiting for approval. Ask the approver to send it back to change it.");
    }

    private void Reset()
    {
        Status = PayrollStatus.Draft;
        SubmittedBy = ApprovedBy = null;
        SubmittedAt = ApprovedAt = null;
    }

    public string Describe() => Status switch
    {
        PayrollStatus.Submitted => "waiting for approval",
        _ => Status.ToString().ToLowerInvariant(),
    };
}

/// <summary>
/// One person's pay in a run, as calculated. A snapshot: names, numbers and payment details as they were, so locked
/// payslips and bank files don't change when the employee's record does. Not audited row by row (the run's status
/// changes are); account numbers are encrypted.
/// </summary>
[NoAudit]
public class Payslip : TenantEntity
{
    private Payslip() { }

    public Payslip(Guid runId, Employee e, string? departmentName, string? positionTitle, PayResult pay)
    {
        RunId = runId;
        EmployeeId = e.Id;
        EmployeeNumber = e.EmployeeNumber;
        EmployeeName = e.FullName;
        DepartmentName = departmentName;
        PositionTitle = positionTitle;
        TaxNumber = e.TaxNumber;
        SocialSecurityNumber = e.SocialSecurityNumber;
        PaymentMethod = e.PaymentMethod;
        BankName = e.BankName;
        BankBranch = e.BankBranch;
        BankAccountNumber = e.BankAccountNumber;
        BankAccountName = e.BankAccountName;
        MobileMoneyProvider = e.MobileMoneyProvider;
        MobileMoneyNumber = e.MobileMoneyNumber;
        SetPay(pay);
    }

    /// <summary>A reversal's copy of a locked payslip: every amount negated.</summary>
    public static Payslip ReversalOf(Guid runId, Payslip original) => new()
    {
        RunId = runId, EmployeeId = original.EmployeeId, EmployeeNumber = original.EmployeeNumber, EmployeeName = original.EmployeeName,
        DepartmentName = original.DepartmentName, PositionTitle = original.PositionTitle, TaxNumber = original.TaxNumber,
        SocialSecurityNumber = original.SocialSecurityNumber, PaymentMethod = original.PaymentMethod, BankName = original.BankName,
        BankBranch = original.BankBranch, BankAccountNumber = original.BankAccountNumber, BankAccountName = original.BankAccountName,
        MobileMoneyProvider = original.MobileMoneyProvider, MobileMoneyNumber = original.MobileMoneyNumber,
        Lines = original.Lines.Select(l => l.Negated()).ToList(), Gross = -original.Gross, TaxableGross = -original.TaxableGross,
        PensionableGross = -original.PensionableGross, TotalDeductions = -original.TotalDeductions, Net = -original.Net,
        EmployerContributions = -original.EmployerContributions, Warnings = [],
    };

    public Guid RunId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string EmployeeNumber { get; private set; } = null!;
    public string EmployeeName { get; private set; } = null!;
    public string? DepartmentName { get; private set; }
    public string? PositionTitle { get; private set; }
    public string? TaxNumber { get; private set; }
    public string? SocialSecurityNumber { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public string? BankName { get; private set; }
    public string? BankBranch { get; private set; }

    [Encrypted]
    public string? BankAccountNumber { get; private set; }

    public string? BankAccountName { get; private set; }
    public string? MobileMoneyProvider { get; private set; }

    [Encrypted]
    public string? MobileMoneyNumber { get; private set; }

    public List<PayLine> Lines { get; private set; } = [];
    public decimal Gross { get; private set; }
    public decimal TaxableGross { get; private set; }
    public decimal PensionableGross { get; private set; }

    /// <summary>Statutory items and other deductions: everything between gross and net.</summary>
    public decimal TotalDeductions { get; private set; }

    public decimal Net { get; private set; }
    public decimal EmployerContributions { get; private set; }
    public List<string> Warnings { get; private set; } = [];

    /// <summary>When the payslip was emailed to the employee (null: not yet).</summary>
    public DateTimeOffset? EmailedAt { get; private set; }

    public void MarkEmailed(DateTimeOffset at) => EmailedAt = at;

    /// <summary>Keeps the amounts and removes who was paid and where, once the retention period has passed.</summary>
    public void Anonymise(string name)
    {
        EmployeeName = name;
        (TaxNumber, SocialSecurityNumber, BankName, BankBranch, BankAccountNumber, BankAccountName, MobileMoneyProvider, MobileMoneyNumber) =
            (null, null, null, null, null, null, null, null);
    }

    private void SetPay(PayResult pay)
    {
        Lines = pay.Lines.ToList();
        Gross = pay.Gross;
        TaxableGross = pay.TaxableGross;
        PensionableGross = pay.PensionableGross;
        TotalDeductions = pay.TotalDeductions;
        Net = pay.Net;
        EmployerContributions = pay.EmployerContributions;
        Warnings = pay.Warnings.ToList();
    }
}

/// <summary>A one-off amount for one person in one run: overtime, a bonus, a deduction for a loss.</summary>
public class PayrollInput : TenantEntity
{
    private PayrollInput() { }

    public PayrollInput(Guid runId, Guid employeeId, Guid componentId, decimal amount, string? note, Countries.CountryPack country)
    {
        Amounts.Check(amount, country, allowZero: false);
        RunId = runId;
        EmployeeId = employeeId;
        ComponentId = componentId;
        Amount = amount;
        Note = Guard.Optional(note);
    }

    public Guid RunId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid ComponentId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Note { get; private set; }
}
