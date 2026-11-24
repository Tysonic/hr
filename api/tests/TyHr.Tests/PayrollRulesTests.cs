using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Payroll;

namespace TyHr.Tests;

/// <summary>
/// Statutory payroll against worked examples. Uganda's tables are URA's (ura.go.ug PAYE rates); Kenya's 100,000
/// example matches published 2026 payroll guides (NSSF 6,000, SHIF 2,750, Housing Levy 1,500, PAYE 19,308.35).
/// </summary>
public class PayrollRulesTests
{
    private static readonly DateOnly Nov2026 = new(2026, 11, 30), Jun2026 = new(2026, 6, 30), Sep2026 = new(2026, 9, 30);

    private static Dictionary<string, decimal> Uganda(decimal gross, DateOnly month, bool resident = true, bool secondary = false) =>
        CountryPacks.Uganda.Payroll.Calculate(gross, gross, gross, new StatutoryContext(month, resident, secondary)).ToDictionary(l => l.Code, l => l.Amount);

    private static Dictionary<string, decimal> Kenya(decimal gross, DateOnly month, bool secondary = false) =>
        CountryPacks.Kenya.Payroll.Calculate(gross, gross, gross, new StatutoryContext(month, true, secondary)).ToDictionary(l => l.Code, l => l.Amount);

    [Theory]
    [InlineData(335_000, 0)]
    [InlineData(400_000, 13_000)]          // 20% of 65,000
    [InlineData(485_000, 33_750)]          // URA: 15,000 + 25% of 75,000
    [InlineData(1_000_000, 188_250)]       // 33,750 + 30% of 515,000
    [InlineData(12_000_000, 3_688_250)]    // 33,750 + 30% of 11,515,000 + 10% of 2,000,000
    public void Uganda_resident_PAYE_follows_URAs_table_from_July_2026(decimal gross, decimal paye) =>
        Assert.Equal(paye, Uganda(gross, Nov2026)["PAYE"]);

    [Theory]
    [InlineData(235_000, 0)]
    [InlineData(335_000, 10_000)]
    [InlineData(1_000_000, 202_000)]       // 25,000 + 30% of 590,000
    public void Uganda_PAYE_before_July_2026_used_the_235000_threshold(decimal gross, decimal paye) =>
        Assert.Equal(paye, Uganda(gross, Jun2026)["PAYE"]);

    [Fact]
    public void Uganda_non_residents_pay_from_the_first_shilling_and_secondary_employment_is_a_flat_30_percent()
    {
        Assert.Equal(33_500, Uganda(335_000, Nov2026, resident: false)["PAYE"]);
        Assert.Equal(225_500, Uganda(1_000_000, Nov2026, resident: false)["PAYE"]); // 48,500 + 30% of 590,000
        Assert.Equal(150_000, Uganda(500_000, Nov2026, secondary: true)["PAYE"]);
    }

    [Fact]
    public void Uganda_NSSF_is_5_percent_from_the_employee_and_10_from_the_employer_and_doesnt_reduce_PAYE()
    {
        var lines = Uganda(1_000_000, Nov2026);
        Assert.Equal((50_000m, 100_000m, 188_250m), (lines["NSSF"], lines["NSSF_ER"], lines["PAYE"]));
        Assert.False(lines.ContainsKey("LST")); // November: LST is collected July to October
    }

    [Theory]
    [InlineData(100_000, 0)]
    [InlineData(250_000, 2_500)]       // 10,000 a year
    [InlineData(1_000_000, 22_500)]    // 90,000 a year
    [InlineData(1_000_001, 25_000)]    // 100,000 a year
    public void Uganda_local_service_tax_is_a_quarter_of_the_years_band_from_July_to_October(decimal gross, decimal lst) =>
        Assert.Equal(lst, Uganda(gross, Sep2026).GetValueOrDefault("LST"));

    [Fact]
    public void Kenya_100000_gross_matches_the_published_worked_example()
    {
        var lines = Kenya(100_000, Nov2026);
        Assert.Equal(6_000m, lines["NSSF"]);        // Tier I 540 + Tier II 5,460
        Assert.Equal(2_750m, lines["SHIF"]);
        Assert.Equal(1_500m, lines["AHL"]);
        Assert.Equal(19_308.35m, lines["PAYE"]);    // on 89,750 less relief of 2,400
        Assert.Equal(70_441.65m, 100_000 - lines["NSSF"] - lines["SHIF"] - lines["AHL"] - lines["PAYE"]);
        Assert.Equal((6_000m, 1_500m, 50m), (lines["NSSF_ER"], lines["AHL_ER"], lines["NITA"]));
    }

    [Fact]
    public void Kenya_NSSF_is_capped_at_the_upper_earnings_limit_which_rose_in_February_2026()
    {
        Assert.Equal(6_480m, Kenya(300_000, Nov2026)["NSSF"]);               // 6% of 108,000
        Assert.Equal(4_320m, Kenya(300_000, new DateOnly(2025, 11, 30))["NSSF"]); // 6% of 72,000
        Assert.Equal(360m, Kenya(6_000, Nov2026)["NSSF"]);                   // below the lower limit: Tier I only, 6% of 6,000
    }

    [Fact]
    public void Kenya_low_pay_owes_no_PAYE_after_relief_but_SHIF_has_a_300_minimum()
    {
        var lines = Kenya(10_000, Nov2026);
        Assert.Equal((0m, 300m), (lines["PAYE"], lines["SHIF"]));
    }

    [Fact]
    public void Kenya_secondary_employment_is_taxed_at_35_percent_without_relief()
    {
        var lines = Kenya(50_000, Nov2026, secondary: true);
        var taxable = 50_000 - lines["NSSF"] - lines["SHIF"] - lines["AHL"];
        Assert.Equal(decimal.Round(taxable * 0.35m, 2), lines["PAYE"]);
    }

    [Fact]
    public void Months_before_the_packs_rates_are_refused()
    {
        Assert.Contains("start from", Assert.Throws<DomainException>(() => Kenya(100_000, new DateOnly(2024, 12, 31))).Message);
    }

    [Fact]
    public void A_bonus_in_a_second_run_is_taxed_on_the_months_total_less_what_was_already_deducted()
    {
        var context = new StatutoryContext(Nov2026, true, false);
        PayLine Earning(decimal amount) => new("BASIC", "Basic", PayLineKind.Earning, amount, true, true);
        var salary = PayCalculator.Calculate(CountryPacks.Uganda, context, [Earning(1_000_000)], [], EarlierPay.None);
        var earlier = new EarlierPay(salary.Gross, salary.TaxableGross, salary.PensionableGross,
            salary.Lines.Where(l => l.Kind is PayLineKind.Statutory or PayLineKind.Employer).ToDictionary(l => l.Code, l => l.Amount));
        var bonus = PayCalculator.Calculate(CountryPacks.Uganda, context, [Earning(500_000)], [], earlier);
        var paye = bonus.Lines.Single(l => l.Code == "PAYE").Amount;
        Assert.Equal(150_000m, paye); // all of it at 30%: 188,250 on 1,000,000 → 338,250 on 1,500,000
        Assert.Equal(500_000 - 150_000 - 25_000, bonus.Net);

        // Reversing the salary run leaves only the bonus's share.
        var reversed = salary.Lines.Select(l => l.Negated()).ToList();
        Assert.Equal(-salary.Net, reversed.Where(l => l.Kind == PayLineKind.Earning).Sum(l => l.Amount)
            - reversed.Where(l => l.Kind == PayLineKind.Statutory).Sum(l => l.Amount));
    }

    [Fact]
    public void Deductions_larger_than_pay_are_flagged()
    {
        var result = PayCalculator.Calculate(CountryPacks.Uganda, new StatutoryContext(Nov2026, true, false),
            [new("BASIC", "Basic", PayLineKind.Earning, 300_000, true, true)], [new("LOAN", "Loan", PayLineKind.Deduction, 400_000)], EarlierPay.None);
        Assert.Contains("negative", Assert.Single(result.Warnings));
    }

    [Fact]
    public void Joiners_are_paid_for_the_calendar_days_they_were_employed()
    {
        Assert.Equal(1m, PayCalculator.EmployedShare(new DateOnly(2025, 1, 1), new DateOnly(2026, 11, 1), Nov2026));
        Assert.Equal(0.5m, PayCalculator.EmployedShare(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 1), Nov2026));
    }
}
