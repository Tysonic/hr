using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TyHr.Application.Common;
using TyHr.Infrastructure.Mail;

namespace TyHr.Tests;

/// <summary>Against a real SMTP server with no sign-in, e.g. the dev Mailpit: TYHR_SMTP=localhost:1025 dotnet test --filter SmtpTests.</summary>
public class SmtpTests
{
    public sealed class SmtpFactAttribute : FactAttribute
    {
        public SmtpFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TYHR_SMTP"))) Skip = "Set TYHR_SMTP to host:port of a test mail server.";
        }
    }

    [SmtpFact]
    public async Task Many_messages_go_over_one_connection_and_a_bad_address_doesnt_stop_the_rest()
    {
        var parts = Environment.GetEnvironmentVariable("TYHR_SMTP")!.Split(':');
        IEmailSender sender = new SmtpEmailSender(Options.Create(new MailOptions { Host = parts[0], Port = int.Parse(parts[1]), Security = "None" }),
            NullLogger<SmtpEmailSender>.Instance);
        var run = Guid.NewGuid().ToString("N")[..6];
        var messages = Enumerable.Range(1, 25).Select(i => new EmailMessage([i == 7 ? "not an address" : $"batch{i}@smtp.test"], $"Batch {run} #{i}", "<p>Hi</p>", "Hi",
            [new EmailAttachment("a.pdf", "application/pdf", "%PDF-1.7"u8.ToArray())])).ToList();
        var results = new List<(int, Exception?)>();
        Assert.True(await sender.SendManyAsync(messages, (i, e) => { results.Add((i, e)); return Task.CompletedTask; }));
        Assert.Equal(25, results.Count);
        Assert.Equal([6], results.Where(r => r.Item2 != null).Select(r => r.Item1));
        Assert.True(await sender.SendAsync(messages[0]));
    }
}
