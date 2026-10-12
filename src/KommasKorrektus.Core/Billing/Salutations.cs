namespace KommasKorrektus.Core.Billing;

/// <summary>The line that opens a letter, from what is known about the recipient.</summary>
public static class Salutations
{
    public static string Letter(string? salutation, string? firstName, string? lastName, bool isCompany)
    {
        var last = lastName?.Trim();
        var first = firstName?.Trim();
        switch (salutation?.Trim().ToLowerInvariant())
        {
            case "frau" when !string.IsNullOrEmpty(last):
                return $"Sehr geehrte Frau {last},";
            case "herr" when !string.IsNullOrEmpty(last):
                return $"Sehr geehrter Herr {last},";
        }

        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
        {
            return $"Guten Tag {first} {last},";
        }

        return isCompany || string.IsNullOrEmpty(last) ? "Sehr geehrte Damen und Herren," : $"Guten Tag {last},";
    }

    public static IReadOnlyList<string> Options { get; } = ["Frau", "Herr", "Divers"];
}
