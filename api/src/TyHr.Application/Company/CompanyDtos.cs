using TyHr.Application.Countries;
using TyHr.Domain.Tenants;

namespace TyHr.Application.Company;

public record CompanyDto(
    string Slug,
    string Name,
    CountryDto Country,
    string LegalName,
    string? TradingName,
    string? TaxNumber,
    string? SocialSecurityNumber,
    string Email,
    string? Phone,
    string? PhysicalAddress,
    string? PostalAddress,
    int FinancialYearStartMonth)
{
    public static CompanyDto From(Tenant t, CompanyProfile p) => new(t.Slug, t.Name, CountryDto.From(t.Country),
        p.LegalName, p.TradingName, p.TaxNumber, p.SocialSecurityNumber, p.Email, p.Phone, p.PhysicalAddress,
        p.PostalAddress, p.FinancialYearStartMonth);
}

public record UpdateCompanyRequest(
    string Name,
    string LegalName,
    string? TradingName,
    string? TaxNumber,
    string? SocialSecurityNumber,
    string Email,
    string? Phone,
    string? PhysicalAddress,
    string? PostalAddress,
    int FinancialYearStartMonth);
