using KommaKorrektus.Configuration;
using KommaKorrektus.Core.Security;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services.Mail;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Services.Auth;

public sealed record LoginCodeCheck(User? User, bool Expired, bool TooManyAttempts);

/// <summary>
/// Sign in without a password: a six-digit code arrives by e-mail and is valid for a few minutes, for five attempts, once.
/// The page that asks for the address never says whether the address is known (the same page follows either way).
/// </summary>
public sealed class LoginCodeService(KkDbContext db, MailService mail, ConfigStore config, TimeProvider clock, IWebHostEnvironment env)
{
    private const int MaxAttempts = 5;

    private const int MaxCodesPer15Minutes = 3;

    /// <summary>E-mail codes need a way to deliver them: a mail server, or the development environment (where the mail log shows the code).</summary>
    public bool IsAvailable => config.Current.Security.AllowEmailCodeLogin && (mail.IsConfigured || env.IsDevelopment());

    /// <summary>Sends a code if the address belongs to an active user. Returns the token of the flow either way.</summary>
    public async Task<Guid> RequestAsync(string? email, CancellationToken ct = default)
    {
        var flow = Guid.NewGuid();
        var normalized = SignInService.NormalizeEmail(email);
        var user = await db.User.FirstOrDefaultAsync(u => u.Email == normalized && u.IsActive, ct);
        if (user is null || !IsAvailable)
        {
            return flow;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var recent = await db.LoginCode.CountAsync(c => c.UserId == user.Id && c.CreateDate > now.AddMinutes(-15), ct);
        if (recent >= MaxCodesPer15Minutes)
        {
            return flow;
        }

        var code = PasswordHasher.RandomDigits(6);
        var minutes = Math.Max(1, config.Current.Security.LoginCodeMinutes);
        db.LoginCode.Add(new LoginCode
        {
            Token = flow,
            UserId = user.Id,
            CodeHash = PasswordHasher.HashToken(code, flow.ToString()),
            ExpiresDate = now.AddMinutes(minutes),
            CreateUserId = user.Id,
        });
        await db.SaveChangesAsync(ct);

        var text = $"Hallo {user.DisplayName},\n\nDein Anmeldecode für KommaKorrektus lautet:\n\n    {code}\n\n" +
                   $"Er ist {minutes} Minuten gültig und funktioniert nur einmal. Wenn du dich nicht angemeldet hast, kannst du diese Nachricht ignorieren.\n";
        await mail.SendAsync(user.Email, "Dein Anmeldecode", text, MailKind.LoginCode, logBody: env.IsDevelopment() ? null : "[Der Code wird aus Sicherheitsgründen nicht gespeichert.]", ct: ct);
        return flow;
    }

    public async Task<LoginCodeCheck> VerifyAsync(Guid flow, string? input, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var entry = await db.LoginCode.Include(c => c.User).ThenInclude(u => u!.Totp).FirstOrDefaultAsync(c => c.Token == flow && c.UsedDate == null, ct);
        if (entry?.User is null || !entry.User.IsActive)
        {
            return new LoginCodeCheck(null, false, false);
        }

        if (entry.ExpiresDate <= now)
        {
            return new LoginCodeCheck(null, true, false);
        }

        if (entry.Attempts >= MaxAttempts)
        {
            return new LoginCodeCheck(null, false, true);
        }

        var digits = new string((input ?? string.Empty).Where(char.IsDigit).ToArray());
        if (PasswordHasher.HashToken(digits, flow.ToString()) != entry.CodeHash)
        {
            entry.Attempts++;
            await db.SaveChangesAsync(ct);
            return new LoginCodeCheck(null, false, entry.Attempts >= MaxAttempts);
        }

        entry.UsedDate = now;
        await db.SaveChangesAsync(ct);
        return new LoginCodeCheck(entry.User, false, false);
    }
}
