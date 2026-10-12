namespace KommasKorrektus.Data.Entities;

public enum CustomerKind
{
    Person,
    Company,
}

public class Customer : AuditedEntity, ISoftDeletable
{
    /// <summary>"K-0001": handed out by <see cref="NumberSequence"/>, never reused.</summary>
    public string Number { get; set; } = "";

    public CustomerKind Kind { get; set; } = CustomerKind.Person;

    public string? Company { get; set; }

    public string? Salutation { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    /// <summary>Authors often write under a pen name; the list shows both.</summary>
    public string? PenName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Street { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    /// <summary>ISO 3166 alpha-2 ("DE").</summary>
    public string Country { get; set; } = "DE";

    public string? VatId { get; set; }

    public string? TaxNumber { get; set; }

    /// <summary>Leitweg-ID or another buyer reference that an e-invoice for a public body needs.</summary>
    public string? BuyerReference { get; set; }

    public int? PaymentTermDays { get; set; }

    public decimal? HourlyRate { get; set; }

    public string? Notes { get; set; }

    public bool IsArchived { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;

    public List<CustomerContact> Contacts { get; set; } = [];

    public List<Project> Projects { get; set; } = [];

    /// <summary>The name as it is written on a letter: the company, else the person.</summary>
    public string DisplayName => Kind == CustomerKind.Company && !string.IsNullOrWhiteSpace(Company)
        ? Company!
        : PersonName;

    public string PersonName => string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public class CustomerContact : AuditedEntity
{
    public long CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public string Name { get; set; } = "";

    public string? Role { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public bool IsPrimary { get; set; }

    public string? Notes { get; set; }
}

public enum HistoryKind
{
    Note,
    Call,
    Mail,
    Meeting,
    /// <summary>Written by the application: a project was created, an invoice was sent, a link was shared.</summary>
    System,
}

/// <summary>The history of a customer: what was said and what happened, newest first on the page.</summary>
public class HistoryEntry : AuditedEntity
{
    public long CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public long? ProjectId { get; set; }

    public HistoryKind Kind { get; set; } = HistoryKind.Note;

    public string Title { get; set; } = "";

    public string? Text { get; set; }

    public DateTime OccurredDate { get; set; }

    /// <summary>What a system entry is about ("Invoice", "Project", "ShareLink") and its id, so the page can link to it.</summary>
    public string? RefType { get; set; }

    public long? RefId { get; set; }
}
