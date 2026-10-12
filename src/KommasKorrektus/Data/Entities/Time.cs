namespace KommasKorrektus.Data.Entities;

public enum TimeEntryKind
{
    /// <summary>Made by the editor itself from the keystrokes while a document is open.</summary>
    Auto,
    /// <summary>A running or finished stop watch.</summary>
    Timer,
    /// <summary>Typed in afterwards.</summary>
    Manual,
}

/// <summary>
/// A block of working time. An automatic entry is closed when there is no activity for longer than the idle limit (a long pause),
/// the next keystroke opens a new one.
/// </summary>
public class TimeEntry : AuditedEntity, ISoftDeletable
{
    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    public long? DocumentId { get; set; }

    public long? TaskId { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    public DateTime StartDate { get; set; }

    /// <summary>The last moment of activity (or the end of the stop watch).</summary>
    public DateTime EndDate { get; set; }

    /// <summary>Seconds that count: the time with activity, without the pauses inside the block.</summary>
    public int Seconds { get; set; }

    public TimeEntryKind Kind { get; set; } = TimeEntryKind.Manual;

    /// <summary>True while a stop watch runs.</summary>
    public bool IsRunning { get; set; }

    public string? Note { get; set; }

    public bool IsBillable { get; set; } = true;

    /// <summary>The invoice line this time was billed on; billed time is not billed twice.</summary>
    public long? InvoiceLineId { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

/// <summary>
/// "How many words were corrected, and when": one row per report of the editor (about every half minute while it is used).
/// </summary>
public class WorkActivity : AuditedEntity
{
    public long UserId { get; set; }

    public long ProjectId { get; set; }

    public long DocumentId { get; set; }

    public long? TimeEntryId { get; set; }

    /// <summary>The end of the reported interval.</summary>
    public DateTime At { get; set; }

    public int ActiveSeconds { get; set; }

    public int WordsAdded { get; set; }

    public int WordsRemoved { get; set; }

    public int WordsChanged { get; set; }

    public int Keystrokes { get; set; }
}
