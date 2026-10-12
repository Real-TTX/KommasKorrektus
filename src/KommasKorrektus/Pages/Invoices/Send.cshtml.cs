using KommasKorrektus.Configuration;
using KommasKorrektus.Data;
using KommasKorrektus.Pages.Billing;
using KommasKorrektus.Services;
using KommasKorrektus.Services.Mail;
using KommasKorrektus.Storage;

namespace KommasKorrektus.Pages.Invoices;

public sealed class SendModel(KkDbContext db, BillingService billing, FileStoreService files, MailService mail, ConfigStore config, ActivityLogger log) : DocSendModelBase(db, billing, files, mail, config, log)
{
    protected override string Base => "/Invoices";
}
