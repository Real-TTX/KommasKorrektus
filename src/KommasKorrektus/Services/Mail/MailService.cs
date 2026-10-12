using KommasKorrektus.Configuration;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace KommasKorrektus.Services.Mail;

public sealed record MailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record MailResult(MailStatus Status, string? Error = null)
{
    public bool Delivered => Status == MailStatus.Sent;
}

/// <summary>
/// Sends mail through the SMTP server of the configuration and keeps a line in <see cref="MailLog"/> for every mail. Without a server the mail is only logged
/// (status "not configured"): the pages that need a mail (invitation, share link) show the link as well, so nothing depends on a mail server being there.
/// </summary>
public sealed class MailService(ConfigStore config, SecretProtector secrets, IServiceScopeFactory scopes, TimeProvider clock, ILogger<MailService> logger)
{
    public bool IsConfigured
    {
        get
        {
            var mail = config.Current.Mail;
            return mail.Enabled && !string.IsNullOrWhiteSpace(mail.Host) && !string.IsNullOrWhiteSpace(mail.FromAddress);
        }
    }

    /// <param name="logBody">The text that is kept in the log; null keeps the real text. A mail with a one-time code keeps no code.</param>
    public async Task<MailResult> SendAsync(string to, string subject, string textBody, MailKind kind, IReadOnlyList<MailAttachment>? attachments = null, string? logBody = null, CancellationToken ct = default)
    {
        MailResult result;
        if (!IsConfigured)
        {
            result = new MailResult(MailStatus.NotConfigured, "Kein E-Mail-Server eingerichtet.");
        }
        else
        {
            try
            {
                await DeliverAsync(to, subject, textBody, attachments, ct);
                result = new MailResult(MailStatus.Sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Sending a mail to {To} failed.", to);
                result = new MailResult(MailStatus.Failed, ex.Message);
            }
        }

        await WriteLogAsync(to, subject, result.Status == MailStatus.NotConfigured || logBody is null ? textBody : logBody, kind, result);
        return result;
    }

    /// <summary>Connects with the given settings and sends nothing: for the "test connection" button.</summary>
    public async Task<string?> TestConnectionAsync(MailConfig mail, string? plainPassword, CancellationToken ct = default)
    {
        try
        {
            using var client = new SmtpClient { Timeout = 15000 };
            await client.ConnectAsync(mail.Host, mail.Port, Options(mail.Security), ct);
            if (!string.IsNullOrEmpty(mail.UserName))
            {
                await client.AuthenticateAsync(mail.UserName, plainPassword ?? secrets.Unprotect(mail.PasswordProtected), ct);
            }

            await client.DisconnectAsync(true, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex.Message;
        }
    }

    private async Task DeliverAsync(string to, string subject, string textBody, IReadOnlyList<MailAttachment>? attachments, CancellationToken ct)
    {
        var mail = config.Current.Mail;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(mail.FromName, mail.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var body = new BodyBuilder { TextBody = textBody };
        foreach (var file in attachments ?? [])
        {
            body.Attachments.Add(file.FileName, file.Content, ContentType.Parse(file.ContentType));
        }

        message.Body = body.ToMessageBody();

        using var client = new SmtpClient { Timeout = 30000 };
        await client.ConnectAsync(mail.Host, mail.Port, Options(mail.Security), ct);
        if (!string.IsNullOrEmpty(mail.UserName))
        {
            await client.AuthenticateAsync(mail.UserName, secrets.Unprotect(mail.PasswordProtected), ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    private static SecureSocketOptions Options(MailSecurity security) => security switch
    {
        MailSecurity.Ssl => SecureSocketOptions.SslOnConnect,
        MailSecurity.StartTls => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.None,
    };

    private async Task WriteLogAsync(string to, string subject, string body, MailKind kind, MailResult result)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KkDbContext>();
            db.MailLog.Add(new MailLog
            {
                SentDate = clock.GetUtcNow().UtcDateTime,
                ToAddress = to,
                Subject = subject,
                BodyText = body,
                Kind = kind,
                Status = result.Status,
                Error = result.Error,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The mail log could not be written.");
        }
    }
}
