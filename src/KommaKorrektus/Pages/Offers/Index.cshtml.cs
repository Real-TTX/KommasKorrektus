using KommaKorrektus.Data;
using KommaKorrektus.Pages.Billing;
using KommaKorrektus.Services;

namespace KommaKorrektus.Pages.Offers;

public sealed class IndexModel(KkDbContext db, BillingService billing, TimeProvider clock) : DocIndexModelBase(db, billing, clock)
{
    protected override bool IsOffers => true;
}
