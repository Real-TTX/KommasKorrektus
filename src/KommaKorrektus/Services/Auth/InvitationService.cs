using KommaKorrektus.Configuration;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services.Mail;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Services.Auth;

public enum InvitationState
{
    Valid,
    Expired,
    Revoked,
    Accepted,
    Unknown,
}

public sealed record InvitationCheck(InvitationState State, Invitation? Invitation);

/// <summary>
/// Invitations: a link with a token that is valid for a limited time. The invited person registers (with a password, or without one and signs in
/// with an e-mail code from then on) and is given access to the customer. A link works once.
/// </summary>
public sealed class InvitationService(KkDbContext db, MailService mail, ConfigStore config, PublicUrl urls, TimeProvider clock, UserService users)
{
    public async Task<Invitation> CreateAsync(string email, string? displayName, UserRole role, long? customerId, string? note, CancellationToken ct = default)
    {
        var invitation = new Invitation
        {
            Email = SignInService.NormalizeEmail(email),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            Role = role,
            CustomerId = role == UserRole.Client ? customerId : null,
            ExpiresDate = clock.GetUtcNow().UtcDateTime.AddDays(Math.Max(1, config.Current.Security.InvitationDays)),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        db.Invitation.Add(invitation);
        await db.SaveChangesAsync(ct);
        return invitation;
    }

    public string LinkOf(Invitation invitation) => urls.Absolute("/Account/Register?token=" + invitation.Token);

    public async Task<MailResult> SendAsync(Invitation invitation, string senderName, CancellationToken ct = default)
    {
        var customer = invitation.CustomerId is { } id ? await db.Customer.FirstOrDefaultAsync(c => c.Id == id, ct) : null;
        var company = config.Current.Company.Name;
        var text = $"Hallo{(invitation.DisplayName is null ? "" : " " + invitation.DisplayName)},\n\n" +
                   $"{senderName} lädt dich zu KommaKorrektus ein" +
                   (customer is null ? "" : $" und gibt dir Zugriff auf die Projekte von {customer.DisplayName}") + ".\n\n" +
                   $"Hier kannst du dich registrieren (der Link ist bis {Fmt.Date(invitation.ExpiresDate)} gültig und funktioniert einmal):\n{LinkOf(invitation)}\n\n" +
                   "Du kannst dir ein Passwort wählen oder dich künftig ohne Passwort mit einem Code per E-Mail anmelden.\n" +
                   (string.IsNullOrWhiteSpace(invitation.Note) ? "" : $"\n{invitation.Note}\n") +
                   (string.IsNullOrWhiteSpace(company) ? "" : $"\n{company}\n");
        var result = await mail.SendAsync(invitation.Email, "Einladung zu KommaKorrektus", text, MailKind.Invitation, ct: ct);
        if (result.Delivered)
        {
            invitation.SentDate = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }

        return result;
    }

    public async Task<InvitationCheck> CheckAsync(Guid token, CancellationToken ct = default)
    {
        var invitation = await db.Invitation.Include(i => i.Customer).FirstOrDefaultAsync(i => i.Token == token, ct);
        if (invitation is null)
        {
            return new InvitationCheck(InvitationState.Unknown, null);
        }

        var state = invitation.AcceptedDate is not null ? InvitationState.Accepted
            : invitation.RevokedDate is not null ? InvitationState.Revoked
            : invitation.ExpiresDate <= clock.GetUtcNow().UtcDateTime ? InvitationState.Expired
            : InvitationState.Valid;
        return new InvitationCheck(state, invitation);
    }

    /// <summary>Makes the user and marks the invitation as used. A null password means: e-mail codes only.</summary>
    public async Task<User> AcceptAsync(Invitation invitation, string displayName, string? password, CancellationToken ct = default)
    {
        var customers = invitation.CustomerId is { } id ? new[] { id } : [];
        var user = await users.CreateAsync(invitation.Email, displayName, invitation.Role, password, customers, ct);
        invitation.AcceptedDate = clock.GetUtcNow().UtcDateTime;
        invitation.AcceptedUserId = user.Id;
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>An existing user accepts an invitation for another customer: the access is added, no second account.</summary>
    public async Task GrantAsync(Invitation invitation, User user, CancellationToken ct = default)
    {
        if (invitation.CustomerId is { } id && !await db.UserCustomer.AnyAsync(c => c.UserId == user.Id && c.CustomerId == id, ct))
        {
            db.UserCustomer.Add(new UserCustomer { UserId = user.Id, CustomerId = id });
        }

        invitation.AcceptedDate = clock.GetUtcNow().UtcDateTime;
        invitation.AcceptedUserId = user.Id;
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAsync(Invitation invitation, CancellationToken ct = default)
    {
        invitation.RevokedDate = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }
}
