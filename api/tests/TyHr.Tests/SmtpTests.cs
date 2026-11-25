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

    [Fact]
    public async Task A_server_that_cant_be_reached_stops_the_batch_at_the_first_message()
    {
        // A port nothing listens on: taken, then released.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        IEmailSender sender = new SmtpEmailSender(Options.Create(new MailOptions { Host = "127.0.0.1", Port = port, Security = "None" }), NullLogger<SmtpEmailSender>.Instance);
        var message = new EmailMessage(["a@smtp.test"], "Hi", "<p>Hi</p>", "Hi");
        var tried = 0;
        await Assert.ThrowsAsync<MailUnavailableException>(() => sender.SendManyAsync([message, message], (_, _) => { tried++; return Task.CompletedTask; }));
        Assert.Equal(0, tried);
        await Assert.ThrowsAsync<MailUnavailableException>(() => sender.SendAsync(message));
    }
}
