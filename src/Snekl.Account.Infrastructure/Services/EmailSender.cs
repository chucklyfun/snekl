using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Snekl.Account.Infrastructure.Services;

public interface IEmailSender
{
    Task SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken ct = default);
    Task SendEmailVerificationAsync(string toEmail, string verifyUrl, CancellationToken ct = default);
}

public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Password reset requested for {Email}. Reset link: {ResetUrl}",
            toEmail,
            resetUrl);
        return Task.CompletedTask;
    }

    public Task SendEmailVerificationAsync(string toEmail, string verifyUrl, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Email verification requested for {Email}. Verify link: {VerifyUrl}",
            toEmail,
            verifyUrl);
        return Task.CompletedTask;
    }
}

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken ct = default)
    {
        var host = _config["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        var port = int.Parse(_config["Smtp:Port"] ?? "587");
        var username = _config["Smtp:Username"];
        var password = _config["Smtp:Password"];
        var useTls = bool.Parse(_config["Smtp:UseTls"] ?? "true");
        var fromAddress = _config["Email:FromAddress"] ?? "noreply@snekl.local";
        var fromName = _config["Email:FromName"] ?? "Snekl Auth";

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = useTls,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password);

        var from = new MailAddress(fromAddress, fromName);
        var to = new MailAddress(toEmail);
        using var message = new MailMessage(from, to)
        {
            Subject = "Reset your Snekl password",
            Body = $"""
                You requested a password reset for your Snekl account.

                Click the link below to choose a new password (valid for 1 hour):

                {resetUrl}

                If you did not request this, you can ignore this email.
                """,
            IsBodyHtml = false
        };

        await client.SendMailAsync(message, ct);
        _logger.LogInformation("Password reset email sent to {Email}", toEmail);
    }

    public async Task SendEmailVerificationAsync(string toEmail, string verifyUrl, CancellationToken ct = default)
    {
        var host = _config["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        var port = int.Parse(_config["Smtp:Port"] ?? "587");
        var username = _config["Smtp:Username"];
        var password = _config["Smtp:Password"];
        var useTls = bool.Parse(_config["Smtp:UseTls"] ?? "true");
        var fromAddress = _config["Email:FromAddress"] ?? "noreply@snekl.local";
        var fromName = _config["Email:FromName"] ?? "Snekl Auth";

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = useTls,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password);

        var from = new MailAddress(fromAddress, fromName);
        var to = new MailAddress(toEmail);
        using var message = new MailMessage(from, to)
        {
            Subject = "Verify your Snekl email",
            Body = $"""
                Please verify your email address for Snekl.

                Click the link below to confirm (valid for 24 hours):

                {verifyUrl}

                If you did not create an account, you can ignore this email.
                """,
            IsBodyHtml = false
        };

        await client.SendMailAsync(message, ct);
        _logger.LogInformation("Verification email sent to {Email}", toEmail);
    }
}
