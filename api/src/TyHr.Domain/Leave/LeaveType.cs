using TyHr.Domain.Employees;

namespace TyHr.Domain.Leave;

/// <summary>
/// How days of a leave type are earned. Monthly: DaysPerYear/12 at the end of each month of service. Yearly: the
/// whole year's days at the start of the leave year (pro rata in the year someone joins). NoBalance: nothing is
/// earned or counted down (sick, maternity, unpaid); each request may still be capped by MaxDaysPerRequest.
/// </summary>
public enum LeaveAccrual { Monthly, Yearly, NoBalance }

public class LeaveType : TenantEntity
{
    private LeaveType() { }

    public LeaveType(string code, string name, LeaveAccrual accrual, decimal? daysPerYear, decimal? maxDaysPerRequest, decimal carryForwardMax,
        bool countsCalendarDays, bool isPaid, Gender? eligibleGender, int minServiceMonths, int? documentRequiredAfterDays, string? description)
    {
        Update(code, name, accrual, daysPerYear, maxDaysPerRequest, carryForwardMax, countsCalendarDays, isPaid, eligibleGender,
            minServiceMonths, documentRequiredAfterDays, description);
        IsActive = true;
    }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public LeaveAccrual Accrual { get; private set; }

    /// <summary>Days earned per leave year (Monthly and Yearly only).</summary>
    public decimal? DaysPerYear { get; private set; }

    public decimal? MaxDaysPerRequest { get; private set; }

    /// <summary>Unused days that move into the next leave year, at most (0 = none carry over).</summary>
    public decimal CarryForwardMax { get; private set; }

    /// <summary>Counts every calendar day (Kenyan maternity) instead of working days without public holidays.</summary>
    public bool CountsCalendarDays { get; private set; }

    public bool IsPaid { get; private set; }
    public Gender? EligibleGender { get; private set; }

    /// <summary>Months of service before the type can be used (and, for balances, before anything is earned).</summary>
    public int MinServiceMonths { get; private set; }

    /// <summary>Requests longer than this many days need a supporting document (e.g. a medical certificate).</summary>
    public int? DocumentRequiredAfterDays { get; private set; }

    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    public bool HasBalance => Accrual != LeaveAccrual.NoBalance;

    public void Update(string code, string name, LeaveAccrual accrual, decimal? daysPerYear, decimal? maxDaysPerRequest, decimal carryForwardMax,
        bool countsCalendarDays, bool isPaid, Gender? eligibleGender, int minServiceMonths, int? documentRequiredAfterDays, string? description)
    {
        Code = Guard.Required(code, "Code").ToUpperInvariant();
        Name = Guard.Required(name, "Name");
        if (accrual == LeaveAccrual.NoBalance)
        {
            daysPerYear = null;
            carryForwardMax = 0;
        }
        else if (daysPerYear is not > 0 || daysPerYear > 366) throw new DomainException($"{Name} needs the number of days earned per year.");
        if (maxDaysPerRequest is <= 0) throw new DomainException("The most days per request must be more than zero, or left empty.");
        if (carryForwardMax < 0) throw new DomainException("Days carried forward can't be negative.");
        if (minServiceMonths is < 0 or > 120) throw new DomainException("Months of service before use must be between 0 and 120.");
        if (documentRequiredAfterDays is < 0) throw new DomainException("Days before a document is needed can't be negative.");
        Accrual = accrual;
        DaysPerYear = daysPerYear;
        MaxDaysPerRequest = maxDaysPerRequest;
        CarryForwardMax = carryForwardMax;
        CountsCalendarDays = countsCalendarDays;
        IsPaid = isPaid;
        EligibleGender = eligibleGender;
        MinServiceMonths = minServiceMonths;
        DocumentRequiredAfterDays = documentRequiredAfterDays;
        Description = Guard.Optional(description);
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>A day off for everyone in the company. Kept per company, seeded from its country pack.</summary>
public class PublicHoliday : TenantEntity
{
    private PublicHoliday() { }

    public PublicHoliday(DateOnly date, string name)
    {
        Date = date;
        Name = Guard.Required(name, "Name");
    }

    public DateOnly Date { get; private set; }
    public string Name { get; private set; } = null!;
}
