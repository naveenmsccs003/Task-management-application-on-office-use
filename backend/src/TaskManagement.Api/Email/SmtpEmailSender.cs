using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Email;

/// <summary>Identity email sender backed by System.Net.Mail (SMTP or a local pickup directory).</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, IHostEnvironment environment, ILogger<SmtpEmailSender> logger)
    : IEmailSender<ApplicationUser>
{
    private const string ApplicationName = "Task Management System";

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, $"{ApplicationName}: reset your password",
            $"<p>Hello {WebUtility.HtmlEncode(user.DisplayName ?? user.UserName)},</p>" +
            "<p>A password reset was requested for your account. Use the link below to choose a new password. " +
            "The link expires shortly and can be used once.</p>" +
            $"<p><a href=\"{resetLink}\">Reset your password</a></p>" +
            "<p>If you did not request this, you can ignore this email; your password will not change.</p>");

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, $"{ApplicationName}: confirm your email", $"<p><a href=\"{confirmationLink}\">Confirm your email</a></p>");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, $"{ApplicationName}: password reset code", $"<p>Your reset code is {WebUtility.HtmlEncode(resetCode)}.</p>");

    private async Task SendAsync(string to, string subject, string htmlBody)
    {
        var settings = options.Value;
        if (settings.DeliveryMethod == EmailDeliveryMethod.None)
        {
            logger.LogWarning("Email delivery is not configured (Email:DeliveryMethod=None); message '{Subject}' was not sent.", subject);
            return;
        }

        using var message = new MailMessage(settings.From, to, subject, htmlBody) { IsBodyHtml = true };
        using var client = new SmtpClient();
        if (settings.DeliveryMethod == EmailDeliveryMethod.PickupDirectory)
        {
            var directory = Path.GetFullPath(settings.PickupDirectory, environment.ContentRootPath);
            Directory.CreateDirectory(directory);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = directory;
        }
        else
        {
            client.Host = settings.SmtpHost ?? throw new InvalidOperationException("Email:SmtpHost is required for SMTP delivery.");
            client.Port = settings.SmtpPort;
            client.EnableSsl = settings.SmtpEnableSsl;
            if (!string.IsNullOrEmpty(settings.SmtpUserName))
            {
                client.Credentials = new NetworkCredential(settings.SmtpUserName, settings.SmtpPassword);
            }
        }

        await client.SendMailAsync(message);
    }
}
