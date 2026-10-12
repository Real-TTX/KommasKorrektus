using KommasKorrektus.Data;
using KommasKorrektus.Pages.Billing;
using KommasKorrektus.Services;
using KommasKorrektus.Storage;

namespace KommasKorrektus.Pages.Offers;

public sealed class DownloadModel(KkDbContext db, BillingService billing, FileStoreService files, TimeProvider clock) : DocDownloadModelBase(db, billing, files, clock)
{
    protected override string Base => "/Offers";
}
