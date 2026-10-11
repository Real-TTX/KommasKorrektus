using KommaKorrektus.Configuration;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Pages.Billing;
using KommaKorrektus.Services;

namespace KommaKorrektus.Pages.Invoices;

public sealed class EditModel(KkDbContext db, BillingService billing, ConfigStore config, ActivityLogger log, TimeProvider clock) : DocEditModelBase(db, billing, config, log, clock)
{
    protected override DocType Type => DocType.Invoice;
}
