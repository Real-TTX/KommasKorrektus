using System.Linq.Expressions;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Data;

public class KkDbContext(DbContextOptions<KkDbContext> options, ICurrentUser currentUser, TimeProvider clock) : DbContext(options)
{
    public DbSet<User> User => Set<User>();

    public DbSet<UserTotp> UserTotp => Set<UserTotp>();

    public DbSet<UserRecoveryCode> UserRecoveryCode => Set<UserRecoveryCode>();

    public DbSet<UserSession> UserSession => Set<UserSession>();

    public DbSet<LoginCode> LoginCode => Set<LoginCode>();

    public DbSet<Invitation> Invitation => Set<Invitation>();

    public DbSet<UserCustomer> UserCustomer => Set<UserCustomer>();

    public DbSet<Customer> Customer => Set<Customer>();

    public DbSet<CustomerContact> CustomerContact => Set<CustomerContact>();

    public DbSet<HistoryEntry> HistoryEntry => Set<HistoryEntry>();

    public DbSet<Project> Project => Set<Project>();

    public DbSet<ProjectTask> ProjectTask => Set<ProjectTask>();

    public DbSet<TaskTemplate> TaskTemplate => Set<TaskTemplate>();

    public DbSet<TaskTemplateItem> TaskTemplateItem => Set<TaskTemplateItem>();

    public DbSet<StyleEntry> StyleEntry => Set<StyleEntry>();

    public DbSet<Document> Document => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersion => Set<DocumentVersion>();

    public DbSet<DocumentComment> DocumentComment => Set<DocumentComment>();

    public DbSet<StoredFile> StoredFile => Set<StoredFile>();

    public DbSet<TimeEntry> TimeEntry => Set<TimeEntry>();

    public DbSet<WorkActivity> WorkActivity => Set<WorkActivity>();

    public DbSet<Invoice> Invoice => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLine => Set<InvoiceLine>();

    public DbSet<Payment> Payment => Set<Payment>();

    public DbSet<Product> Product => Set<Product>();

    public DbSet<InvoiceTemplate> InvoiceTemplate => Set<InvoiceTemplate>();

    public DbSet<TextBlock> TextBlock => Set<TextBlock>();

    public DbSet<NumberSequence> NumberSequence => Set<NumberSequence>();

    public DbSet<ShareLink> ShareLink => Set<ShareLink>();

    public DbSet<ShareAccess> ShareAccess => Set<ShareAccess>();

    public DbSet<ActivityLog> ActivityLog => Set<ActivityLog>();

    public DbSet<MailLog> MailLog => Set<MailLog>();

    public DbSet<BackupTarget> BackupTarget => Set<BackupTarget>();

    public DbSet<BackupPlan> BackupPlan => Set<BackupPlan>();

    public DbSet<BackupRun> BackupRun => Set<BackupRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Tables are PascalCase and named like the entity; enums are stored as text; money has two decimals.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            entityType.SetTableName(entityType.ClrType.Name);

            foreach (var property in entityType.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                // UpdateState is a number on purpose (0 = deleted, 1 = created, 2 = updated); DayOfWeek is one too.
                if (type.IsEnum && type != typeof(DayOfWeek) && type != typeof(UpdateState))
                {
                    property.SetProviderClrType(typeof(string));
                }
                else if (type == typeof(decimal) && property.GetColumnType() is null)
                {
                    property.SetColumnType("numeric(18,2)");
                }
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                b.Entity(entityType.ClrType).HasQueryFilter(NotDeleted(entityType.ClrType));
            }
        }

        // Prices and quantities need more than two decimals.
        b.Entity<InvoiceLine>().Property(x => x.Quantity).HasColumnType("numeric(18,4)");
        b.Entity<InvoiceLine>().Property(x => x.UnitPrice).HasColumnType("numeric(18,4)");
        b.Entity<InvoiceLine>().Property(x => x.VatRate).HasColumnType("numeric(5,2)");
        b.Entity<InvoiceLine>().Property(x => x.DiscountPercent).HasColumnType("numeric(5,2)");
        b.Entity<Invoice>().Property(x => x.DiscountPercent).HasColumnType("numeric(5,2)");
        b.Entity<Product>().Property(x => x.NetPrice).HasColumnType("numeric(18,4)");
        b.Entity<Product>().Property(x => x.VatRate).HasColumnType("numeric(5,2)");
        b.Entity<Project>().Property(x => x.Rate).HasColumnType("numeric(18,4)");
        b.Entity<Customer>().Property(x => x.HourlyRate).HasColumnType("numeric(18,2)");
        b.Entity<Project>().Property(x => x.EstimatedHours).HasColumnType("numeric(9,2)");

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique().HasFilter("\"UpdateState\" <> 0");
            e.HasOne(x => x.Totp).WithOne(x => x.User).HasForeignKey<UserTotp>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<UserTotp>().HasIndex(x => x.UserId).IsUnique();
        b.Entity<UserRecoveryCode>().HasOne(x => x.UserTotp).WithMany(x => x.RecoveryCodes).HasForeignKey(x => x.UserTotpId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserSession>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<LoginCode>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Invitation>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<UserCustomer>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CustomerId }).IsUnique();
            e.HasOne(x => x.User).WithMany(x => x.Customers).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Customer>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.Contacts).WithOne(x => x.Customer).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Projects).WithOne(x => x.Customer).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<HistoryEntry>(e =>
        {
            e.HasIndex(x => new { x.CustomerId, x.OccurredDate });
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Project>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.ResponsibleUser).WithMany().HasForeignKey(x => x.ResponsibleUserId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<ProjectTask>(e =>
        {
            e.HasIndex(x => x.ProjectId);
            e.HasOne(x => x.Project).WithMany(x => x.Tasks).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<TaskTemplateItem>().HasOne(x => x.TaskTemplate).WithMany(x => x.Items).HasForeignKey(x => x.TaskTemplateId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<StyleEntry>().HasOne(x => x.Project).WithMany(x => x.StyleEntries).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Document>(e =>
        {
            e.HasIndex(x => x.ProjectId);
            e.HasOne(x => x.Project).WithMany(x => x.Documents).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.OriginalFile).WithMany().HasForeignKey(x => x.OriginalFileId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<DocumentVersion>(e =>
        {
            e.HasIndex(x => new { x.DocumentId, x.VersionNo }).IsUnique();
            e.HasOne(x => x.Document).WithMany(x => x.Versions).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SourceFile).WithMany().HasForeignKey(x => x.SourceFileId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<DocumentComment>(e =>
        {
            e.HasIndex(x => x.DocumentId);
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<StoredFile>().HasIndex(x => x.Token).IsUnique();

        b.Entity<TimeEntry>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.StartDate });
            e.HasIndex(x => x.ProjectId);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WorkActivity>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.At });
            e.HasIndex(x => new { x.ProjectId, x.At });
        });

        b.Entity<Invoice>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique().HasFilter("\"Number\" IS NOT NULL");
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => new { x.Type, x.Status });
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Lines).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Payments).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.IsLocked);
        });
        b.Entity<Product>().HasIndex(x => x.Number).IsUnique();
        b.Entity<NumberSequence>().HasIndex(x => x.Key).IsUnique();

        b.Entity<ShareLink>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ShareAccess>().HasOne(x => x.ShareLink).WithMany(x => x.Accesses).HasForeignKey(x => x.ShareLinkId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ActivityLog>().HasIndex(x => x.OccurredDate);
        b.Entity<MailLog>().HasIndex(x => x.SentDate);
        b.Entity<BackupPlan>().HasOne(x => x.Target).WithMany().HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BackupRun>().HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Customer>().Ignore(x => x.DisplayName).Ignore(x => x.PersonName);
    }

    private static LambdaExpression NotDeleted(Type clrType)
    {
        var parameter = Expression.Parameter(clrType, "e");
        var state = Expression.Property(parameter, nameof(ISoftDeletable.UpdateState));
        var body = Expression.NotEqual(state, Expression.Constant(UpdateState.Deleted));
        return Expression.Lambda(body, parameter);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Fills the audit columns and turns a delete of a soft-deletable record into a state change.</summary>
    private void Stamp()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var userId = currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries<AuditedEntity>().ToList())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable soft)
            {
                entry.State = EntityState.Modified;
                soft.UpdateState = UpdateState.Deleted;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreateDate == default)
                    {
                        entry.Entity.CreateDate = now;
                    }

                    entry.Entity.CreateUserId ??= userId;
                    entry.Entity.UpdateDate = entry.Entity.CreateDate;
                    entry.Entity.UpdateUserId = entry.Entity.CreateUserId;
                    if (entry.Entity is ISoftDeletable added && added.UpdateState == UpdateState.Deleted)
                    {
                        added.UpdateState = UpdateState.Created;
                    }

                    break;

                case EntityState.Modified:
                    entry.Entity.UpdateDate = now;
                    entry.Entity.UpdateUserId = userId;
                    entry.Property(nameof(AuditedEntity.CreateDate)).IsModified = false;
                    entry.Property(nameof(AuditedEntity.CreateUserId)).IsModified = false;
                    if (entry.Entity is ISoftDeletable modified && modified.UpdateState == UpdateState.Created)
                    {
                        modified.UpdateState = UpdateState.Updated;
                    }

                    break;
            }
        }
    }
}
