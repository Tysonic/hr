using TyHr.Domain.Countries;

namespace TyHr.Application.Countries;

public record CountryDto(string Code, string Name, string CurrencyCode, int CurrencyMinorUnits, string PhonePrefix,
    string TaxNumberLabel, string SocialSecurityLabel, string NationalIdLabel)
{
    public static CountryDto From(CountryPack c) =>
        new(c.Code, c.Name, c.CurrencyCode, c.CurrencyMinorUnits, c.PhonePrefix, c.TaxNumberLabel, c.SocialSecurityLabel, c.Formats.NationalIdLabel);

    public static IReadOnlyList<CountryDto> AllPacks() => CountryPacks.All.Select(From).ToList();
}
