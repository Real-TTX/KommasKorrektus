using KommasKorrektus.Data;
using KommasKorrektus.Pages.Billing;
using KommasKorrektus.Services;

namespace KommasKorrektus.Pages.Invoices;

public sealed class DetailsModel(KkDbContext db, BillingService billing, ActivityLogger log) : DocDetailsModelBase(db, billing, log)
{
    protected override bool IsOfferPage => false;
}
