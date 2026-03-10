namespace MyAspNetApp.Services
{
    public interface IEmailService
    {
        // Sends an HTML email to the given address.
        Task SendEmailAsync(string toEmail, string subject, string htmlBody);
    }
}
