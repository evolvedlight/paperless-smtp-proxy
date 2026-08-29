using SmtpServer;
using SmtpServer.Mail;
using SmtpServer.Storage;
using Microsoft.Extensions.Options;
using HomeSmtpServer.Models;
using HomeSmtpServer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace HomeSmtpServer.Services;

public class CustomMailboxFilter : MailboxFilter
{
    private readonly IOptionsMonitor<SmtpServerSettings> _settings;
    private readonly ILogger<CustomMailboxFilter> _logger;
    private readonly IHubContext<MailHub> _hubContext;

    public CustomMailboxFilter(
        IOptionsMonitor<SmtpServerSettings> settings, 
        ILogger<CustomMailboxFilter> logger,
        IHubContext<MailHub> hubContext)
    {
        _settings = settings;
        _logger = logger;
        _hubContext = hubContext;
    }

    public override Task<bool> CanAcceptFromAsync(
        ISessionContext context,
        IMailbox @from,
        int size,
        CancellationToken cancellationToken)
    {
        var localPort = context.EndpointDefinition?.Endpoint?.Port.ToString() ?? "25";
        var sender = @from?.AsAddress() ?? "(none)";
        _logger.LogInformation("📥 [SMTP INBOUND] Port {Port} -> MAIL FROM: <{Sender}> (Declared Size: {Size} bytes)", localPort, sender, size);

        _ = _hubContext.Clients.All.SendAsync("ServerLog", new
        {
            Level = "Info",
            Message = $"[SMTP Inbound] Port :{localPort} -> MAIL FROM: <{sender}>",
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);

        return Task.FromResult(true);
    }

    public override Task<bool> CanDeliverToAsync(
        ISessionContext context,
        IMailbox to,
        IMailbox @from,
        CancellationToken cancellationToken)
    {
        var localPort = context.EndpointDefinition?.Endpoint?.Port.ToString() ?? "25";
        var config = _settings.CurrentValue;
        var recipientAddress = to.AsAddress();
        var recipientHost = to.Host;

        _logger.LogInformation("📥 [SMTP INBOUND] Port {Port} -> RCPT TO: <{Recipient}>", localPort, recipientAddress);

        if (config.AllowAnyRecipient)
        {
            return Task.FromResult(true);
        }

        var isRecipientAllowed = config.AllowedRecipients.Any(r => string.Equals(r, recipientAddress, StringComparison.OrdinalIgnoreCase));
        var isDomainAllowed = config.AllowedDomains.Any(d => string.Equals(d, recipientHost, StringComparison.OrdinalIgnoreCase));

        if (isRecipientAllowed || isDomainAllowed)
        {
            return Task.FromResult(true);
        }

        _logger.LogWarning("⛔ [SMTP INBOUND REJECT] Rejected recipient <{Recipient}> from <{Sender}> (Port {Port}) - Not configured in allowed list", 
            recipientAddress, @from?.AsAddress(), localPort);

        _ = _hubContext.Clients.All.SendAsync("ServerLog", new
        {
            Level = "Warning",
            Message = $"[SMTP Reject] Recipient <{recipientAddress}> rejected (not in allowed list)",
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);

        return Task.FromResult(false);
    }
}
