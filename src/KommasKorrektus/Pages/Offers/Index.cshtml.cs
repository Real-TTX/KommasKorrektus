using KommasKorrektus.Data;
using KommasKorrektus.Pages.Billing;
using KommasKorrektus.Services;

namespace KommasKorrektus.Pages.Offers;

public sealed class IndexModel(KkDbContext db, BillingService billing, TimeProvider clock) : DocIndexModelBase(db, billing, clock)
{
    protected override bool IsOffers => true;
}
