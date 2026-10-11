using System.Globalization;

namespace KommaKorrektus.Core.Billing;

/// <summary>The way amounts and dates are written on a German document.</summary>
public static class GermanFormat
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Money(decimal amount, string currency = "EUR")
        => amount.ToString("N2", De) + " " + (currency == "EUR" ? "€" : currency);

    /// <summary>A quantity without needless zeros: 75, 1,5, 0,25.</summary>
    public static string Quantity(decimal value) => value.ToString("#,0.####", De);

    public static string Percent(decimal value) => value.ToString("0.##", De) + " %";

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", De);

    public static string Date(DateOnly? date) => date is null ? string.Empty : Date(date.Value);

    /// <summary>"11.10.2026" or "01.10.2026 bis 31.10.2026".</summary>
    public static string Period(DateOnly? from, DateOnly? to)
        => from is null && to is null ? string.Empty : to is null || from == to ? Date(from ?? to) : from is null ? Date(to) : $"{Date(from)} bis {Date(to)}";
}
