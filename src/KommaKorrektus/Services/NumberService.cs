using System.Text.RegularExpressions;
using KommaKorrektus.Configuration;
using KommaKorrektus.Data;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Services;

public enum NumberKind
{
    Customer,
    Project,
    Product,
    Offer,
    Invoice,
    CreditNote,
}

/// <summary>
/// Hands out the numbers of customers, projects, offers and invoices. The counter is incremented by one SQL statement (insert or update with
/// <c>RETURNING</c>), so two requests never get the same number; inside a transaction a rolled-back number is given back, so the numbers of invoices have no gaps.
/// </summary>
public sealed partial class NumberService(KkDbContext db, ConfigStore config, TimeProvider clock)
{
    public async Task<string> NextAsync(NumberKind kind, CancellationToken ct = default)
    {
        var format = FormatOf(kind);
        var today = Fmt.Today(clock);
        var key = format.Contains("{YYYY}") || format.Contains("{YY}") ? $"{kind}:{today.Year}" : kind.ToString();
        var value = await NextValueAsync(key, ct);
        return Format(format, today, value);
    }

    /// <summary>Looks at the next number without taking it (for the placeholder of a form).</summary>
    public async Task<string> PeekAsync(NumberKind kind, CancellationToken ct = default)
    {
        var format = FormatOf(kind);
        var today = Fmt.Today(clock);
        var key = format.Contains("{YYYY}") || format.Contains("{YY}") ? $"{kind}:{today.Year}" : kind.ToString();
        var next = await db.NumberSequence.Where(s => s.Key == key).Select(s => (long?)s.NextValue).FirstOrDefaultAsync(ct) ?? 1;
        return Format(format, today, next);
    }

    public static string Format(string format, DateOnly date, long value)
    {
        var text = format.Replace("{YYYY}", date.Year.ToString("0000")).Replace("{YY}", (date.Year % 100).ToString("00")).Replace("{MM}", date.Month.ToString("00"));
        return CounterRegex().Replace(text, m => value.ToString(new string('0', m.Groups[1].Length)));
    }

    private string FormatOf(NumberKind kind)
    {
        var company = config.Current.Company;
        return kind switch
        {
            NumberKind.Customer => company.CustomerNumberFormat,
            NumberKind.Project => company.ProjectNumberFormat,
            NumberKind.Product => company.ProductNumberFormat,
            NumberKind.Offer => company.OfferNumberFormat,
            NumberKind.Invoice => company.InvoiceNumberFormat,
            _ => company.CreditNoteNumberFormat,
        };
    }

    private async Task<long> NextValueAsync(string key, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.Database.SqlQuery<long>($"""
            WITH taken AS (
                INSERT INTO "NumberSequence" ("Key", "NextValue", "CreateDate", "UpdateDate")
                VALUES ({key}, 2, {now}, {now})
                ON CONFLICT ("Key") DO UPDATE SET "NextValue" = "NumberSequence"."NextValue" + 1, "UpdateDate" = {now}
                RETURNING "NextValue"
            )
            SELECT "NextValue" - 1 AS "Value" FROM taken
            """).ToListAsync(ct);
        return rows.Single();
    }

    [GeneratedRegex("\\{(N+)\\}")]
    private static partial Regex CounterRegex();
}
