using KommaKorrektus.Data;
using KommaKorrektus.Pages.Billing;
using KommaKorrektus.Services;
using KommaKorrektus.Storage;

namespace KommaKorrektus.Pages.Offers;

public sealed class DownloadModel(KkDbContext db, BillingService billing, FileStoreService files, TimeProvider clock) : DocDownloadModelBase(db, billing, files, clock)
{
    protected override string Base => "/Offers";
}
