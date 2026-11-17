using System.Net;
using System.Net.Mail;

namespace Crm.Api.Services;

public sealed class ConfiguredEmailSender(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<ConfiguredEmailSender> logger) : IAppEmailSender
{
    public async Task SendPasswordResetAsync(string recipient, string resetToken, CancellationToken cancellationToken)
    {
        var host = configuration["Email:SmtpHost"];
        if (string.IsNullOrWhiteSpace(host))
        {
            if (environment.IsDevelopment())
            {
                logger.LogInformation(
                    "Development password reset for {Recipient}. Use the token from this local log only: {ResetToken}",
                    recipient,
                    resetToken);
                return;
            }

            logger.LogWarning("Password reset was requested, but SMTP is not configured.");
            return;
        }

        var from = configuration["Email:From"];
        if (string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException("Email:From must be configured when SMTP is enabled.");
        }

        using var message = new MailMessage(from, recipient)
        {
            Subject = "AI Customer Intelligence password reset",
            Body = $"Use this one-time password reset token in the application: {resetToken}",
            IsBodyHtml = false
        };
        using var client = new SmtpClient(host, configuration.GetValue<int?>("Email:SmtpPort") ?? 587)
        {
            EnableSsl = configuration.GetValue("Email:EnableSsl", true),
            Credentials = new NetworkCredential(configuration["Email:Username"], configuration["Email:Password"])
        };
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
