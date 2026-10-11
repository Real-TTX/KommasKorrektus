namespace KommaKorrektus.Data.Entities;

/// <summary>The life of a project, from the first enquiry to the archive.</summary>
public enum ProjectStatus
{
    Inquiry,
    Sample,
    Offered,
    Commissioned,
    InProgress,
    Review,
    Done,
    Archived,
    Cancelled,
}

public enum ServiceType
{
    Proofreading,
    Copyediting,
    Consulting,
    Sample,
    Formatting,
    Other,
}

public enum PricingModel
{
    /// <summary>Per standard page of 1800 characters including spaces: the usual way to quote a manuscript in German.</summary>
    PerNormPage,
    PerWord,
    Hourly,
    Flat,
}

public enum Priority
{
    Low,
    Normal,
    High,
}

public class Project : AuditedEntity, ISoftDeletable
{
    /// <summary>"P-2026-001".</summary>
    public string Number { get; set; } = "";

    public long CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public string? Genre { get; set; }

    public ServiceType ServiceType { get; set; } = ServiceType.Copyediting;

    public ProjectStatus Status { get; set; } = ProjectStatus.Inquiry;

    public PricingModel PricingModel { get; set; } = PricingModel.PerNormPage;

    /// <summary>The price per norm page, per word or per hour, depending on <see cref="PricingModel"/>.</summary>
    public decimal? Rate { get; set; }

    public decimal? FlatPrice { get; set; }

    public decimal? EstimatedHours { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public DateOnly? DoneDate { get; set; }

    public Priority Priority { get; set; } = Priority.Normal;

    public long? ResponsibleUserId { get; set; }

    public User? ResponsibleUser { get; set; }

    public string? Notes { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;

    public List<ProjectTask> Tasks { get; set; } = [];

    public List<Document> Documents { get; set; } = [];

    public List<StyleEntry> StyleEntries { get; set; } = [];
}

public enum TaskState
{
    Open,
    InProgress,
    Done,
}

public class ProjectTask : AuditedEntity, ISoftDeletable
{
    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public TaskState State { get; set; } = TaskState.Open;

    public DateOnly? DueDate { get; set; }

    public long? AssignedUserId { get; set; }

    public User? AssignedUser { get; set; }

    public int? EstimatedMinutes { get; set; }

    public int SortOrder { get; set; }

    public DateTime? CompletedDate { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

/// <summary>A list of tasks that is put on a new project ("Probelektorat", "Korrektorat Roman"...).</summary>
public class TaskTemplate : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Proposed when a project of this kind is created.</summary>
    public ServiceType? ServiceType { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;

    public List<TaskTemplateItem> Items { get; set; } = [];
}

public class TaskTemplateItem : AuditedEntity
{
    public long TaskTemplateId { get; set; }

    public TaskTemplate? TaskTemplate { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public int? EstimatedMinutes { get; set; }

    /// <summary>Days after the start of the project (or after today) when the task is due.</summary>
    public int? DueOffsetDays { get; set; }

    public int SortOrder { get; set; }
}

public enum StyleCategory
{
    Character,
    Place,
    Spelling,
    Term,
    Rule,
    Other,
}

/// <summary>An entry of the style sheet (Stilblatt) of a project: how a name, a word or a rule is handled, so that the whole manuscript stays consistent.</summary>
public class StyleEntry : AuditedEntity, ISoftDeletable
{
    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    public StyleCategory Category { get; set; } = StyleCategory.Spelling;

    public string Term { get; set; } = "";

    public string? Decision { get; set; }

    public string? Note { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}
