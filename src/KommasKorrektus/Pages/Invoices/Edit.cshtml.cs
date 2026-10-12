using KommasKorrektus.Configuration;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Pages.Billing;
using KommasKorrektus.Services;

namespace KommasKorrektus.Pages.Invoices;

public sealed class EditModel(KkDbContext db, BillingService billing, ConfigStore config, ActivityLogger log, TimeProvider clock) : DocEditModelBase(db, billing, config, log, clock)
{
    protected override DocType Type => DocType.Invoice;
}
