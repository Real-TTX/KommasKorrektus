namespace KommasKorrektus.Data.Entities;

/// <summary>The simple role system: administrator, user (the editor), client (a customer with access to the portal). Anonymous is a share link, not a user.</summary>
public enum UserRole
{
    Admin,
    User,
    Client,
}

public class User : AuditedEntity, ISoftDeletable
{
    public string Email { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>Null: this user signs in with an e-mail code only.</summary>
    public string? PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public bool IsActive { get; set; } = true;

    public string? Phone { get; set; }

    /// <summary>"system", "light" or "dark": kept per user, applied at sign-in.</summary>
    public string Theme { get; set; } = "system";

    public DateTime? LastLoginDate { get; set; }

    public int FailedLogins { get; set; }

    public DateTime? LockoutEndDate { get; set; }

    public DateTime? PasswordChangedDate { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;

    public UserTotp? Totp { get; set; }

    public List<UserCustomer> Customers { get; set; } = [];
}

/// <summary>The second factor of a user (RFC 6238). It counts only once <see cref="IsConfirmed"/> is set by a valid first code.</summary>
public class UserTotp : AuditedEntity
{
    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The shared secret, protected with the key ring of the application.</summary>
    public string Secret { get; set; } = "";

    public bool IsConfirmed { get; set; }

    /// <summary>The time step of the last accepted code: a code works once.</summary>
    public long LastUsedStep { get; set; }

    public List<UserRecoveryCode> RecoveryCodes { get; set; } = [];
}

public class UserRecoveryCode : AuditedEntity
{
    public long UserTotpId { get; set; }

    public UserTotp? UserTotp { get; set; }

    public string CodeHash { get; set; } = "";

    public DateTime? UsedDate { get; set; }
}

/// <summary>A signed-in browser. The cookie carries only <see cref="Token"/>; the session is valid as long as this row is.</summary>
public class UserSession : AuditedEntity
{
    public Guid Token { get; set; } = Guid.NewGuid();

    public long UserId { get; set; }

    public User? User { get; set; }

    public DateTime ExpiresDate { get; set; }

    public DateTime LastSeenDate { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime? RevokedDate { get; set; }
}

/// <summary>A one-time code sent by e-mail: sign in without a password.</summary>
public class LoginCode : AuditedEntity
{
    public Guid Token { get; set; } = Guid.NewGuid();

    public long UserId { get; set; }

    public User? User { get; set; }

    public string CodeHash { get; set; } = "";

    public DateTime ExpiresDate { get; set; }

    public int Attempts { get; set; }

    public DateTime? UsedDate { get; set; }
}

/// <summary>An invitation: a link that is valid for a limited time and lets the invited person register (or sign in with an e-mail code).</summary>
public class Invitation : AuditedEntity
{
    public Guid Token { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = "";

    public string? DisplayName { get; set; }

    public UserRole Role { get; set; } = UserRole.Client;

    /// <summary>The customer a client gets access to.</summary>
    public long? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public DateTime ExpiresDate { get; set; }

    public DateTime? AcceptedDate { get; set; }

    public long? AcceptedUserId { get; set; }

    public DateTime? RevokedDate { get; set; }

    public DateTime? SentDate { get; set; }

    public string? Note { get; set; }
}

/// <summary>Which customers a client user may see in the portal.</summary>
public class UserCustomer : AuditedEntity
{
    public long UserId { get; set; }

    public User? User { get; set; }

    public long CustomerId { get; set; }

    public Customer? Customer { get; set; }
}
