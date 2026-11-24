namespace TyHr.Domain.Countries;

/// <summary>
/// What a country brings to the system: currency and identifier formats, public holidays and statutory leave defaults.
/// and statutory payroll rules (effective-dated tax bands, social security, levies).
/// </summary>
/// <param name="Code">ISO 3166-1 alpha-2, e.g. "UG".</param>
/// <param name="CurrencyCode">ISO 4217, e.g. "UGX".</param>
/// <param name="CurrencyMinorUnits">Decimal places amounts are kept to (UGX 0, KES 2).</param>
/// <param name="PhonePrefix">International dialling prefix, e.g. "+256".</param>
/// <param name="TaxNumberLabel">What the country calls a taxpayer number, e.g. "TIN" or "KRA PIN".</param>
public sealed record CountryPack(
    string Code,
    string Name,
    string CurrencyCode,
    int CurrencyMinorUnits,
    string PhonePrefix,
    string TaxNumberLabel,
    string SocialSecurityLabel,
    CountryFormats Formats,
    CountryCalendar Calendar,
    Payroll.PayrollRules Payroll,
    ExitRules Exits)
{
    /// <summary>
    /// Local time's offset from UTC. Both packs are on East Africa Time (UTC+3, no daylight saving), so a fixed offset
    /// is exact and needs no time-zone database in the container.
    /// </summary>
    public TimeSpan UtcOffset => TimeSpan.FromHours(3);

    public DateTimeOffset ToLocal(DateTimeOffset utc) => utc.ToOffset(UtcOffset);

    public decimal Round(decimal amount) => decimal.Round(amount, CurrencyMinorUnits, MidpointRounding.AwayFromZero);

    public bool IsValidAmount(decimal amount) => Round(amount) == amount;
}

public static class CountryPacks
{
    public static readonly CountryPack Uganda = new("UG", "Uganda", "UGX", 0, "+256", "TIN", "NSSF number", new UgandaFormats(), new UgandaCalendar(), new Payroll.UgandaPayrollRules(), new UgandaExitRules());
    public static readonly CountryPack Kenya = new("KE", "Kenya", "KES", 2, "+254", "KRA PIN", "NSSF number", new KenyaFormats(), new KenyaCalendar(), new Payroll.KenyaPayrollRules(), new KenyaExitRules());

    public static IReadOnlyList<CountryPack> All { get; } = [Uganda, Kenya];

    public static CountryPack? Find(string? code) =>
        All.FirstOrDefault(c => string.Equals(c.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static CountryPack Get(string code) =>
        Find(code) ?? throw new DomainException($"There is no country pack for '{code}'. Available: {string.Join(", ", All.Select(c => c.Code))}.");
}
