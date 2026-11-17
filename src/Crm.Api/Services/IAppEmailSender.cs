namespace Crm.Api.Services;

public interface IAppEmailSender
{
    Task SendPasswordResetAsync(string recipient, string resetToken, CancellationToken cancellationToken);
}
