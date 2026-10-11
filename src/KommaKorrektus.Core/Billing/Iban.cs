using System.Numerics;
using System.Text;

namespace KommaKorrektus.Core.Billing;

/// <summary>IBAN checks (ISO 13616, mod 97) and the writing of an IBAN in blocks of four.</summary>
public static class Iban
{
    /// <summary>Letters and digits only, upper case.</summary>
    public static string Normalize(string? iban)
        => new((iban ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool IsValid(string? iban)
    {
        var s = Normalize(iban);
        if (s.Length is < 15 or > 34 || !char.IsLetter(s[0]) || !char.IsLetter(s[1]) || !char.IsDigit(s[2]) || !char.IsDigit(s[3]))
        {
            return false;
        }

        var rearranged = s[4..] + s[..4];
        var digits = new StringBuilder();
        foreach (var c in rearranged)
        {
            digits.Append(char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString());
        }

        return BigInteger.Parse(digits.ToString()) % 97 == 1;
    }

    /// <summary>"DE89 3704 0044 0532 0130 00".</summary>
    public static string Format(string? iban)
    {
        var s = Normalize(iban);
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i += 4)
        {
            if (i > 0) { sb.Append(' '); }
            sb.Append(s, i, Math.Min(4, s.Length - i));
        }

        return sb.ToString();
    }
}
