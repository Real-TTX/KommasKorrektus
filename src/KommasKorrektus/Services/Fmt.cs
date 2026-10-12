using System.Globalization;

namespace KommasKorrektus.Services;

/// <summary>How the interface writes dates, times, money and numbers: German, in the time zone of the installation.</summary>
public static class Fmt
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-DE");

    private static TimeZoneInfo _zone = TimeZoneInfo.Utc;

    public static TimeZoneInfo Zone => _zone;

    /// <summary>Called once at start-up with the time zone of the configuration; an unknown name falls back to UTC.</summary>
    public static void Configure(string timeZoneId)
    {
        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _zone = TimeZoneInfo.Utc;
        }
    }

    public static DateTime Local(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(Local(clock.GetUtcNow().UtcDateTime));

    public static DateTime UtcFromLocal(DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone);

    public static string Date(DateOnly? date) => date is null ? "" : date.Value.ToString("dd.MM.yyyy", Culture);

    public static string Date(DateTime? utc) => utc is null ? "" : Local(utc.Value).ToString("dd.MM.yyyy", Culture);

    public static string Moment(DateTime? utc) => utc is null ? "" : Local(utc.Value).ToString("dd.MM.yyyy HH:mm", Culture);

    /// <summary>A moment as a file states it (the date of a comment in a Word file): shown as written, without a change of time zone; else the moment it was made here.</summary>
    public static string Moment(DateTime made, DateTime? stated) => stated is { } s ? s.ToString("dd.MM.yyyy HH:mm", Culture) : Moment(made);

    public static string Time(DateTime? utc) => utc is null ? "" : Local(utc.Value).ToString("HH:mm", Culture);

    /// <summary>An ISO date for the value of a date input.</summary>
    public static string Iso(DateOnly? date) => date is null ? "" : date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Money(decimal amount, string currency = "EUR")
        => amount.ToString("N2", Culture) + " " + (currency == "EUR" ? "€" : currency);

    public static string Number(decimal value, int decimals = 2) => value.ToString("N" + decimals, Culture);

    public static string Integer(long value) => value.ToString("N0", Culture);

    /// <summary>Seconds as "2:05 h" (hours and minutes).</summary>
    public static string Duration(int seconds)
    {
        var minutes = (int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero);
        return $"{minutes / 60}:{minutes % 60:00} h";
    }

    public static string Hours(int seconds) => (seconds / 3600m).ToString("0.00", Culture);

    public static string FileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size.ToString("0.#", Culture)} {units[unit]}";
    }

    /// <summary>"vor 5 Minuten" up to a week, then the date.</summary>
    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var span = nowUtc - utc;
        if (span.TotalSeconds < 60) { return "gerade eben"; }
        if (span.TotalMinutes < 60) { return $"vor {(int)span.TotalMinutes} Min."; }
        if (span.TotalHours < 24) { return $"vor {(int)span.TotalHours} Std."; }
        if (span.TotalDays < 7) { return $"vor {(int)span.TotalDays} Tg."; }
        return Date(utc);
    }
}
