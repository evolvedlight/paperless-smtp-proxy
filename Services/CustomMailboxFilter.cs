using SmtpServer;
using SmtpServer.Mail;
using SmtpServer.Storage;
using Microsoft.Extensions.Options;
using HomeSmtpServer.Models;

namespace HomeSmtpServer.Services;

public class CustomMailboxFilter : MailboxFilter
{
    private readonly IOptionsMonitor<SmtpServerSettings> _settings;
    private readonly ILogger<CustomMailboxFilter> _logger;

    public CustomMailboxFilter(IOptionsMonitor<SmtpServerSettings> settings, ILogger<CustomMailboxFilter> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public override Task<bool> CanAcceptFromAsync(
        ISessionContext context,
        IMailbox @from,
        int size,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }

    public override Task<bool> CanDeliverToAsync(
        ISessionContext context,
        IMailbox to,
        IMailbox @from,
        CancellationToken cancellationToken)
    {
        var config = _settings.CurrentValue;

        if (config.AllowAnyRecipient)
        {
            return Task.FromResult(true);
        }

        var recipientAddress = to.AsAddress();
        var recipientHost = to.Host;

        var isRecipientAllowed = config.AllowedRecipients.Any(r => string.Equals(r, recipientAddress, StringComparison.OrdinalIgnoreCase));
        var isDomainAllowed = config.AllowedDomains.Any(d => string.Equals(d, recipientHost, StringComparison.OrdinalIgnoreCase));

        if (isRecipientAllowed || isDomainAllowed)
        {
            return Task.FromResult(true);
        }

        _logger.LogWarning("Rejected recipient {Recipient} from {Sender} - Recipient/domain not configured", recipientAddress, @from?.AsAddress());
        return Task.FromResult(false);
    }
}
