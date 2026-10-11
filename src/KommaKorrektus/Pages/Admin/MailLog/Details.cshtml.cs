using KommaKorrektus.Data;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Admin.MailLog;

/// <summary>One logged mail with its text.</summary>
public sealed class DetailsModel(KkDbContext db) : PageModelBase
{
    public Data.Entities.MailLog Mail { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var mail = await db.MailLog.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (mail is null)
        {
            return NotFound();
        }

        Mail = mail;
        Head(mail.Subject, $"An {mail.ToAddress} · {Fmt.Moment(mail.SentDate)}", ("Verwaltung", null), ("E-Mail-Protokoll", "/Admin/MailLog"), (mail.Subject, null));
        ViewData["ActiveHref"] = "/Admin/MailLog";
        ViewData["Width"] = "narrow";
        return Page();
    }
}
