using System.Text.Json.Serialization;

namespace KommasKorrektus.Configuration;

/// <summary>
/// The configuration (JSON, <c>{data}/config/app.json</c>): how this installation is deployed and what the administrator set up.
/// The sections <see cref="Database"/> and <see cref="Server"/> can be overridden by environment variables
/// (<c>KOMMASKORREKTUS__Database__Host</c>); the rest is edited on the administration pages.
/// </summary>
public sealed class AppConfig
{
    public DatabaseConfig Database { get; set; } = new();

    public ServerConfig Server { get; set; } = new();

    public CompanyConfig Company { get; set; } = new();

    public MailConfig Mail { get; set; } = new();

    public StorageConfig Storage { get; set; } = new();

    public TrackingConfig Tracking { get; set; } = new();

    public SecurityConfig Security { get; set; } = new();

    public BackupConfig Backup { get; set; } = new();
}

public sealed class DatabaseConfig
{
    public string Host { get; set; } = "db";

    public int Port { get; set; } = 5432;

    public string Database { get; set; } = "kommaskorrektus";

    public string Username { get; set; } = "postgres";

    public string Password { get; set; } = "kommaskorrektus";
}

public sealed class ServerConfig
{
    /// <summary>The address people use, for links in mails and on invoices ("https://lektorat.example.de"). Empty: taken from the request.</summary>
    public string PublicUrl { get; set; } = "";

    /// <summary>"Europe/Berlin": the time zone of the installation, for dates, schedules and numbers.</summary>
    public string TimeZone { get; set; } = "Europe/Berlin";

    /// <summary>The cookie is marked secure when the site is reached over HTTPS (also behind a reverse proxy that says so).</summary>
    public bool ForceSecureCookie { get; set; }
}

/// <summary>Who the editor is: the sender of offers and invoices. Everything here ends up on a document.</summary>
public sealed class CompanyConfig
{
    public string Name { get; set; } = "";

    public string Owner { get; set; } = "";

    public string Street { get; set; } = "";

    public string PostalCode { get; set; } = "";

    public string City { get; set; } = "";

    public string Country { get; set; } = "DE";

    public string Email { get; set; } = "";

    public string Phone { get; set; } = "";

    public string Website { get; set; } = "";

    /// <summary>Steuernummer.</summary>
    public string TaxNumber { get; set; } = "";

    /// <summary>Umsatzsteuer-Identifikationsnummer.</summary>
    public string VatId { get; set; } = "";

    /// <summary>Kleinunternehmer (paragraph 19 UStG): invoices show no VAT.</summary>
    public bool SmallBusiness { get; set; }

    public decimal DefaultVatRate { get; set; } = 19m;

    public int DefaultPaymentDays { get; set; } = 14;

    public int OfferValidDays { get; set; } = 30;

    public string Currency { get; set; } = "EUR";

    public string AccountHolder { get; set; } = "";

    public string BankName { get; set; } = "";

    public string Iban { get; set; } = "";

    public string Bic { get; set; } = "";

    public decimal DefaultHourlyRate { get; set; } = 55m;

    /// <summary>Characters of a standard page (Normseite), spaces included.</summary>
    public int NormPageCharacters { get; set; } = 1800;

    /// <summary>Number formats: {YYYY}, {YY}, {NNNN} (the counter, as many N as digits). The counter of invoices restarts every year when {YYYY} or {YY} is in the format.</summary>
    public string CustomerNumberFormat { get; set; } = "K-{NNNN}";

    public string ProjectNumberFormat { get; set; } = "P-{YYYY}-{NNN}";

    public string OfferNumberFormat { get; set; } = "AN-{YYYY}-{NNNN}";

    public string InvoiceNumberFormat { get; set; } = "RE-{YYYY}-{NNNN}";

    public string CreditNoteNumberFormat { get; set; } = "GS-{YYYY}-{NNNN}";

    public string ProductNumberFormat { get; set; } = "A-{NNN}";
}

public enum MailSecurity
{
    None,
    StartTls,
    Ssl,
}

public sealed class MailConfig
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public MailSecurity Security { get; set; } = MailSecurity.StartTls;

    public string UserName { get; set; } = "";

    /// <summary>Protected with the key ring of the application.</summary>
    public string PasswordProtected { get; set; } = "";

    public string FromAddress { get; set; } = "";

    public string FromName { get; set; } = "KommasKorrektus";
}

public enum StorageMode
{
    /// <summary>The files are kept in the data volume.</summary>
    Local,
    /// <summary>The files are kept on a share of a NAS (SMB).</summary>
    Smb,
}

public sealed class StorageConfig
{
    public StorageMode Mode { get; set; } = StorageMode.Local;

    public string Host { get; set; } = "";

    public int Port { get; set; } = 445;

    public string Share { get; set; } = "";

    /// <summary>A folder inside the share.</summary>
    public string Path { get; set; } = "KommasKorrektus";

    public string Domain { get; set; } = "";

    public string UserName { get; set; } = "";

    public string PasswordProtected { get; set; } = "";
}

public sealed class TrackingConfig
{
    /// <summary>A pause longer than this ends a block of working time; the next keystroke starts a new one.</summary>
    public int IdleMinutes { get; set; } = 5;

    /// <summary>
    /// The editor saves its text into the version being worked on after this many seconds without typing (0 = off), and at the latest five times as
    /// long (at least ten seconds) while somebody types without a pause. Saving does not make versions, so it can be done often.
    /// </summary>
    public int AutosaveSeconds { get; set; } = 3;
}

public sealed class SecurityConfig
{
    public int SessionDays { get; set; } = 14;

    public int LoginCodeMinutes { get; set; } = 10;

    public int InvitationDays { get; set; } = 14;

    public bool AllowEmailCodeLogin { get; set; } = true;

    /// <summary>Administrators have to set up an authenticator app.</summary>
    public bool RequireTotpForAdmins { get; set; }

    /// <summary>Default life of a share link, in days.</summary>
    public int ShareLinkDays { get; set; } = 14;

    [JsonIgnore]
    public int MaxFailedLogins => 5;
}

public sealed class BackupConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>The scratch folder of a backup; empty: {data}/tmp.</summary>
    public string TempDirectory { get; set; } = "";
}
