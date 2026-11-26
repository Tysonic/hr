using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using TyHr.Application.Common;

namespace TyHr.Infrastructure.Mail;

public class MailOptions
{
    /// <summary>SMTP server; leave empty to send nothing (reminders then stay pending and are retried).</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    /// <summary>None (local Mailpit), StartTls (port 587) or Ssl (port 465).</summary>
    public string Security { get; set; } = "StartTls";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "TyHR <no-reply@tyhr.local>";
}

/// <summary>Sends through the configured SMTP server with MailKit, like Tycount's mail transport.</summary>
public class SmtpEmailSender(IOptions<MailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var sent = false;
        Exception? error = null;
        var configured = await SendManyAsync([message], (_, e) => { (sent, error) = (e == null, e); return Task.CompletedTask; }, cancellationToken);
        if (error != null) throw error;
        return configured && sent;
    }

    /// <summary>One connection for the lot (reconnecting if the server drops it), so hundreds of payslips don't mean hundreds of sign-ins.</summary>
    public async Task<bool> SendManyAsync(IReadOnlyList<EmailMessage> messages, Func<int, Exception?, Task> done, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host))
        {
            logger.LogWarning("No mail server is configured (Mail:Host); not sending {Count} message(s).", messages.Count);
            return false;
        }

        // The certificate's chain and host name are still checked; revocation lists often can't be reached from containers.
        using var client = new SmtpClient { Timeout = 30_000, CheckCertificateRevocation = false };
        for (var i = 0; i < messages.Count; i++)
        {
            if (!client.IsConnected) await ConnectAsync(client, o, cancellationToken);
            Exception? error = null;
            try { await client.SendAsync(ToMime(messages[i], o), cancellationToken); }
            catch (Exception e) when (e is not OperationCanceledException) { error = e; }
            await done(i, error);
        }
        if (client.IsConnected) await client.DisconnectAsync(true, cancellationToken);
        return true;
    }

    private static async Task ConnectAsync(SmtpClient client, MailOptions o, CancellationToken ct)
    {
        try
        {
            await client.ConnectAsync(o.Host!, o.Port, o.Security switch
            {
                "None" => SecureSocketOptions.None,
                "Ssl" => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.StartTls,
            }, ct);
            if (!string.IsNullOrEmpty(o.Username)) await client.AuthenticateAsync(o.Username, o.Password ?? "", ct);
        }
        catch (Exception e) when (e is not OperationCanceledException) { throw new MailUnavailableException(e); }
    }

    private static MimeMessage ToMime(EmailMessage message, MailOptions o)
    {
        var mime = new MimeMessage { Subject = message.Subject };
        mime.From.Add(MailboxAddress.Parse(o.From));
        foreach (var to in message.To) mime.To.Add(MailboxAddress.Parse(to));
        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        foreach (var a in message.Attachments ?? []) body.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        mime.Body = body.ToMessageBody();
        return mime;
    }
}
