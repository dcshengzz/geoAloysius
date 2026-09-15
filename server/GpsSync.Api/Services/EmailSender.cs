namespace GpsSync.Api.Services;

public class EmailOptions
{
    /// <summary>"Dev" (log the email) or "Smtp".</summary>
    public string Mode { get; set; } = "Dev";
    public string From { get; set; } = "no-reply@gpssync.local";
    public string? Host { get; set; }
    public int Port { get; set; } = 25;
    public string? User { get; set; }
    public string? Password { get; set; }
    public bool UseSsl { get; set; }
}

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}

/// <summary>Local-dev sender: logs the email (including any reset code) instead of sending it.</summary>
public class DevEmailSender : IEmailSender
{
    private readonly ILogger<DevEmailSender> _logger;
    public DevEmailSender(ILogger<DevEmailSender> logger) => _logger = logger;

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        _logger.LogWarning("=== DEV EMAIL (not actually sent) ===\nTo: {To}\nSubject: {Subject}\n{Body}\n=====================================",
            toEmail, subject, body);
        return Task.CompletedTask;
    }
}

/// <summary>SMTP sender (set Email:Mode=Smtp + Email:Host/Port/... to enable). Works with a local catcher like Mailpit/smtp4dev.</summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _opt;
    public SmtpEmailSender(EmailOptions opt) => _opt = opt;

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        using var client = new System.Net.Mail.SmtpClient(_opt.Host, _opt.Port) { EnableSsl = _opt.UseSsl };
        if (!string.IsNullOrEmpty(_opt.User))
            client.Credentials = new System.Net.NetworkCredential(_opt.User, _opt.Password);
        using var msg = new System.Net.Mail.MailMessage(_opt.From, toEmail, subject, body);
        await client.SendMailAsync(msg, ct);
    }
}
