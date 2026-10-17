using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Security;

public sealed record AuthMail(string To, string AppName, string Subject, string Body, string? Link = null, string? ButtonText = null);

/// <summary>Sign-in emails (password reset links and change confirmations).</summary>
public interface IAuthMailer
{
    /// <summary>Queues the email and returns at once, so the response time does not show whether an account exists.</summary>
    void Enqueue(AuthMail mail);
}

/// <summary>
/// In-memory queue sent by a background service in the API process. Reset links are never written to the database or to a
/// job store (only the token hash is stored). Uses SMTP (Email:SmtpHost) or, for local testing, a pickup directory
/// (Email:PickupDirectory) where each email is written as an .eml file. With neither configured the email is dropped and a
/// warning is logged (without the link).
/// </summary>
public sealed class AuthMailQueue(IConfiguration config, ILogger<AuthMailQueue> log) : BackgroundService, IAuthMailer
{
    readonly Channel<AuthMail> _queue = Channel.CreateBounded<AuthMail>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Enqueue(AuthMail mail) => _queue.Writer.TryWrite(mail);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var mail in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await SendAsync(mail, stoppingToken); }
            catch (Exception e) when (e is SmtpException or IOException or InvalidOperationException)
            {
                log.LogWarning(e, "Sign-in email \"{Subject}\" to {To} could not be sent", mail.Subject, Mask(mail.To));
            }
        }
    }

    async Task SendAsync(AuthMail mail, CancellationToken ct)
    {
        var host = config["Email:SmtpHost"];
        var pickup = config["Email:PickupDirectory"];
        if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(pickup))
        {
            log.LogWarning("Email is not configured (Email:SmtpHost); the sign-in email \"{Subject}\" to {To} was not sent", mail.Subject, Mask(mail.To));
            return;
        }
        var from = config["Email:From"] ?? "no-reply@localhost";
        using var msg = BrandedEmail.Create(from, mail.To, mail.AppName, mail.Subject, mail.Body, mail.Link, mail.ButtonText);
        using var smtp = string.IsNullOrWhiteSpace(host)
            ? new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = Directory.CreateDirectory(pickup!).FullName }
            : new SmtpClient(host, config.GetValue("Email:SmtpPort", 587)) { EnableSsl = config.GetValue("Email:UseTls", true) };
        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(config["Email:Username"]))
            smtp.Credentials = new System.Net.NetworkCredential(config["Email:Username"], config["Email:Password"]);
        await smtp.SendMailAsync(msg, ct);
    }

    static string Mask(string email) => email.IndexOf('@') is var at and > 1 ? email[0] + new string('*', at - 1) + email[at..] : "***";
}
