using KommaKorrektus.Data;
using KommaKorrektus.Pages.Billing;
using KommaKorrektus.Services;

namespace KommaKorrektus.Pages.Invoices;

public sealed class DetailsModel(KkDbContext db, BillingService billing, ActivityLogger log) : DocDetailsModelBase(db, billing, log)
{
    protected override bool IsOfferPage => false;
}
