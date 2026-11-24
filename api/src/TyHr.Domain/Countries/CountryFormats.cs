using System.Text.RegularExpressions;

namespace TyHr.Domain.Countries;

/// <summary>
/// Validation and normalisation of a country's identifiers (adapted from sacco's UgandanFormats). Each country pack
/// has one; a country without specific rules only trims and upper-cases.
/// </summary>
public abstract partial class CountryFormats
{
    public abstract string NationalIdLabel { get; }

    /// <summary>The normalised national ID number, or a DomainException when it isn't one.</summary>
    public abstract string NormalizeNationalId(string value);

    /// <summary>The normalised taxpayer number (TIN, KRA PIN), or a DomainException when it isn't one.</summary>
    public abstract string NormalizeTaxNumber(string value);

    /// <summary>
    /// A phone number in international form (+256772123456). Local numbers (0772 123456) get the country's prefix;
    /// numbers already starting with + or 00 are kept, for staff with foreign numbers.
    /// </summary>
    public string NormalizePhone(string value, string phonePrefix)
    {
        var trimmed = value.Trim();
        var digits = NonDigits().Replace(trimmed, "");
        if (trimmed.StartsWith('+')) return Checked("+" + digits, value);
        if (digits.StartsWith("00")) return Checked("+" + digits[2..], value);
        var countryDigits = phonePrefix.TrimStart('+');
        if (digits.StartsWith(countryDigits) && digits.Length > countryDigits.Length + 6) return Checked("+" + digits, value);
        if (digits.StartsWith('0')) digits = digits[1..];
        return Checked(phonePrefix + digits, value);
    }

    private static string Checked(string international, string original) =>
        international.Length is >= 9 and <= 16 ? international : throw new DomainException($"'{original}' is not a valid phone number.");

    protected static string Compact(string value) => Spaces().Replace(value, "").ToUpperInvariant();

    [GeneratedRegex(@"\D")]
    protected static partial Regex NonDigits();

    [GeneratedRegex(@"[\s-]")]
    private static partial Regex Spaces();
}

/// <summary>Uganda: NIN on the national ID, URA TIN.</summary>
public sealed partial class UgandaFormats : CountryFormats
{
    public override string NationalIdLabel => "NIN";

    /// <summary>14 characters starting CM (citizen male) or CF (citizen female), e.g. CM90012345ABCD.</summary>
    public override string NormalizeNationalId(string value)
    {
        var nin = Compact(value);
        return Nin().IsMatch(nin) ? nin : throw new DomainException($"'{value}' is not a valid NIN (14 characters starting with CM or CF).");
    }

    /// <summary>URA TIN: 10 digits.</summary>
    public override string NormalizeTaxNumber(string value)
    {
        var tin = NonDigits().Replace(value, "");
        return tin.Length == 10 ? tin : throw new DomainException($"'{value}' is not a valid URA TIN (10 digits).");
    }

    [GeneratedRegex("^C[MF][A-Z0-9]{12}$")]
    private static partial Regex Nin();
}

/// <summary>Kenya: national ID card number, KRA PIN.</summary>
public sealed partial class KenyaFormats : CountryFormats
{
    public override string NationalIdLabel => "National ID number";

    /// <summary>The ID card number: 7 or 8 digits.</summary>
    public override string NormalizeNationalId(string value)
    {
        var id = NonDigits().Replace(value, "");
        return id.Length is 7 or 8 ? id : throw new DomainException($"'{value}' is not a valid Kenyan national ID number (7 or 8 digits).");
    }

    /// <summary>KRA PIN: a letter, 9 digits and a letter, e.g. A123456789B.</summary>
    public override string NormalizeTaxNumber(string value)
    {
        var pin = Compact(value);
        return KraPin().IsMatch(pin) ? pin : throw new DomainException($"'{value}' is not a valid KRA PIN (e.g. A123456789B).");
    }

    [GeneratedRegex("^[AP][0-9]{9}[A-Z]$")]
    private static partial Regex KraPin();
}
