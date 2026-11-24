using TyHr.Domain.Leave;

namespace TyHr.Domain.Tenants;

/// <summary>
/// The company's own details, kept by its HR admins: legal name, tax and statutory registration numbers, contacts,
/// and the month its financial year starts (used by leave years and annual tax returns). One per tenant.
/// </summary>
public class CompanyProfile : TenantEntity
{
    private CompanyProfile() { }

    public CompanyProfile(string legalName, string email)
    {
        LegalName = Guard.Required(legalName, "Legal name");
        Email = Guard.Required(email, "Email").ToLowerInvariant();
        FinancialYearStartMonth = 1;
        WorkingDays = WorkWeek.MondayToFriday.Mask;
    }

    public string LegalName { get; private set; } = null!;
    public string? TradingName { get; private set; }

    /// <summary>Tax identification number (URA TIN in Uganda, KRA PIN in Kenya).</summary>
    public string? TaxNumber { get; private set; }

    /// <summary>Employer's social security registration number (NSSF employer number).</summary>
    public string? SocialSecurityNumber { get; private set; }

    public string Email { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string? PhysicalAddress { get; private set; }
    public string? PostalAddress { get; private set; }
    public int FinancialYearStartMonth { get; private set; }

    /// <summary>The usual working week (<see cref="WorkWeek"/> mask), used to count leave days.</summary>
    public int WorkingDays { get; private set; }

    /// <summary>Years after someone's last working day that their personal data is kept.</summary>
    public int RetentionYears { get; private set; } = 7;

    public void SetRetention(int years)
    {
        if (years is < 5 or > 25) throw new DomainException("Keep former employees' records for 5 to 25 years; tax and employment law need at least 5.");
        RetentionYears = years;
    }

    public WorkWeek WorkWeek => new(WorkingDays);

    public void SetWorkWeek(WorkWeek week) => WorkingDays = WorkWeek.Of(week.Days).Mask;

    public void Update(string legalName, string? tradingName, string? taxNumber, string? socialSecurityNumber,
        string email, string? phone, string? physicalAddress, string? postalAddress, int financialYearStartMonth)
    {
        if (financialYearStartMonth is < 1 or > 12) throw new DomainException("Financial year start month must be 1-12.");
        LegalName = Guard.Required(legalName, "Legal name");
        TradingName = Guard.Optional(tradingName);
        TaxNumber = Guard.Optional(taxNumber)?.ToUpperInvariant();
        SocialSecurityNumber = Guard.Optional(socialSecurityNumber)?.ToUpperInvariant();
        Email = Guard.Required(email, "Email").ToLowerInvariant();
        Phone = Guard.Optional(phone);
        PhysicalAddress = Guard.Optional(physicalAddress);
        PostalAddress = Guard.Optional(postalAddress);
        FinancialYearStartMonth = financialYearStartMonth;
    }

}
