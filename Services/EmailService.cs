using System.Net;
using System.Net.Mail;

namespace MyAspNetApp.Services
{
    // Sends HTML emails via Gmail SMTP using credentials from appsettings.json.
    public class EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            // Read SMTP settings directly from configuration.
            var host     = config["EmailSettings:SmtpHost"]     ?? "smtp.gmail.com";
            var port     = int.TryParse(config["EmailSettings:SmtpPort"], out var p) ? p : 587;
            var from     = config["EmailSettings:SenderEmail"]   ?? string.Empty;
            var password = config["EmailSettings:SenderPassword"] ?? string.Empty;
            var name     = config["EmailSettings:SenderName"]    ?? "NextHorizon";

            // Guard: skip if credentials are missing.
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning(
                    "Email not sent to {Email} — Gmail credentials are not configured in appsettings.json.",
                    toEmail);
                return;
            }

            try
            {
                // Configure the SMTP client for Gmail.
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl   = true,
                    Credentials = new NetworkCredential(from, password)
                };

                // Build the email message.
                using var message = new MailMessage
                {
                    From       = new MailAddress(from, name),
                    Subject    = subject,
                    Body       = htmlBody,
                    IsBodyHtml = true
                };
                message.To.Add(toEmail);

                await client.SendMailAsync(message);
                logger.LogInformation("Email sent to {Email} — subject: {Subject}", toEmail, subject);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send email to {Email} — subject: {Subject}", toEmail, subject);
                throw; // Re-throw so the caller can decide whether to surface the error.
            }
        }
    }
}
