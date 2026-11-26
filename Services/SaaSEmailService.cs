using System.Net;
using System.Net.Mail;
using CrmLeadManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Services;

public interface ISaaSEmailService
{
    Task<(bool Success, string Message)> SendTemplatedEmailAsync(
        string templateCode, 
        string recipientEmail, 
        string recipientName, 
        Dictionary<string, string>? variables = null);

    Task<(bool Success, string Message)> SendCustomEmailAsync(
        string recipientEmail, 
        string subject, 
        string bodyHtml);

    Task<(bool Success, string Message)> SendTestEmailAsync(string recipientEmail);
}

public class SaaSEmailService : ISaaSEmailService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SaaSEmailService> _logger;

    public SaaSEmailService(AppDbContext db, ILogger<SaaSEmailService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(bool Success, string Message)> SendTemplatedEmailAsync(
        string templateCode, 
        string recipientEmail, 
        string recipientName, 
        Dictionary<string, string>? variables = null)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return (false, "Recipient email address is required.");
        }

        try
        {
            var template = await _db.EmailTemplates
                .FirstOrDefaultAsync(t => t.TemplateCode.ToUpper() == templateCode.ToUpper());

            if (template == null)
            {
                _logger.LogWarning("Email template '{TemplateCode}' not found in database.", templateCode);
                return (false, $"Email template '{templateCode}' was not found.");
            }

            var settings = await _db.PlatformSettings.FirstOrDefaultAsync() ?? new SaaSPlatformSettings();

            var subject = template.Subject;
            var bodyHtml = template.BodyHtml ?? "";

            // Merge common fallback variables
            var mergeDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "PlatformName", settings.PlatformName ?? "VisionCRM SaaS" },
                { "SupportEmail", settings.SupportEmail ?? "support@visioncrm.com" },
                { "RecipientName", recipientName },
                { "AdminName", recipientName },
                { "Year", DateTime.UtcNow.Year.ToString() }
            };

            if (variables != null)
            {
                foreach (var (k, v) in variables)
                {
                    mergeDict[k] = v;
                }
            }

            foreach (var (key, val) in mergeDict)
            {
                subject = subject.Replace("{" + key + "}", val ?? "");
                bodyHtml = bodyHtml.Replace("{" + key + "}", val ?? "");
            }

            return await DeliverEmailAsync(recipientEmail, recipientName, subject, bodyHtml, settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render or dispatch templated email '{TemplateCode}' to {Recipient}", templateCode, recipientEmail);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> SendCustomEmailAsync(string recipientEmail, string subject, string bodyHtml)
    {
        var settings = await _db.PlatformSettings.FirstOrDefaultAsync() ?? new SaaSPlatformSettings();
        return await DeliverEmailAsync(recipientEmail, "", subject, bodyHtml, settings);
    }

    public async Task<(bool Success, string Message)> SendTestEmailAsync(string recipientEmail)
    {
        var settings = await _db.PlatformSettings.FirstOrDefaultAsync() ?? new SaaSPlatformSettings();
        var subject = $"[VisionCRM SaaS] SMTP Test Email - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
        var bodyHtml = $@"
            <div style='font-family:Inter,Segoe UI,sans-serif;max-width:540px;margin:0 auto;padding:24px;border:1px solid #e2e8f0;border-radius:12px;'>
                <div style='background:#2563eb;color:#fff;padding:12px 18px;border-radius:8px 8px 0 0;font-size:18px;font-weight:700;'>
                    VisionCRM Email Configuration Test
                </div>
                <div style='padding:20px;background:#f8fafc;border-radius:0 0 8px 8px;'>
                    <p style='color:#166534;font-weight:700;'>✓ SMTP Connection Successful!</p>
                    <p style='color:#334155;font-size:14px;line-height:1.5;'>
                        Your automated email delivery system is properly configured and operational.
                    </p>
                    <ul style='color:#64748b;font-size:13px;line-height:1.6;'>
                        <li><strong>SMTP Host:</strong> {settings.SmtpHost}</li>
                        <li><strong>SMTP Port:</strong> {settings.SmtpPort}</li>
                        <li><strong>SSL Enabled:</strong> {settings.SmtpEnableSsl}</li>
                        <li><strong>From Email:</strong> {settings.SmtpFromEmail}</li>
                        <li><strong>Dispatched At:</strong> {DateTime.UtcNow:dd MMM yyyy HH:mm:ss} UTC</li>
                    </ul>
                </div>
            </div>";

        return await DeliverEmailAsync(recipientEmail, "SuperAdmin", subject, bodyHtml, settings);
    }

    private async Task<(bool Success, string Message)> DeliverEmailAsync(
        string toEmail, 
        string toName, 
        string subject, 
        string bodyHtml, 
        SaaSPlatformSettings settings)
    {
        // If SMTP host is not configured, log simulated delivery for development/sandbox
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            _logger.LogInformation(
                "[MOCK EMAIL DISPATCH] (SMTP Host unconfigured) To: {ToEmail}, Subject: '{Subject}'", 
                toEmail, subject);

            return (true, "Email simulated successfully (SMTP host is not configured, logged to console).");
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(
                    string.IsNullOrWhiteSpace(settings.SmtpFromEmail) ? "notifications@visioncrm.com" : settings.SmtpFromEmail.Trim(),
                    string.IsNullOrWhiteSpace(settings.SmtpFromName) ? settings.PlatformName : settings.SmtpFromName.Trim()
                ),
                Subject = subject,
                Body = bodyHtml,
                IsBodyHtml = true
            };

            message.To.Add(new MailAddress(toEmail.Trim(), toName?.Trim()));

            using var client = new SmtpClient(settings.SmtpHost.Trim(), settings.SmtpPort > 0 ? settings.SmtpPort : 587)
            {
                EnableSsl = settings.SmtpEnableSsl,
                Timeout = 15000 // 15 seconds
            };

            if (!string.IsNullOrWhiteSpace(settings.SmtpUser))
            {
                client.Credentials = new NetworkCredential(settings.SmtpUser.Trim(), settings.SmtpPassword ?? "");
            }

            await client.SendMailAsync(message);

            _logger.LogInformation("Email successfully dispatched via SMTP to {ToEmail} with Subject '{Subject}'", toEmail, subject);
            return (true, $"Email dispatched successfully to {toEmail}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP delivery failed for {ToEmail}: {Error}", toEmail, ex.Message);
            return (false, $"SMTP error: {ex.Message}");
        }
    }
}

