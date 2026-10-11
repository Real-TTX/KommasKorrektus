using KommaKorrektus.Configuration;
using KommaKorrektus.Data;
using KommaKorrektus.Pages.Billing;
using KommaKorrektus.Services;
using KommaKorrektus.Services.Mail;
using KommaKorrektus.Storage;

namespace KommaKorrektus.Pages.Invoices;

public sealed class SendModel(KkDbContext db, BillingService billing, FileStoreService files, MailService mail, ConfigStore config, ActivityLogger log) : DocSendModelBase(db, billing, files, mail, config, log)
{
    protected override string Base => "/Invoices";
}
