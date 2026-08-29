using System.Buffers;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using HomeSmtpServer.Services;
using SmtpServer.Mail;

namespace HomeSmtpServer.Services;

public class CustomMessageStore : MessageStore
{
    private readonly IEmailProcessingService _processingService;
    private readonly ILogger<CustomMessageStore> _logger;

    public CustomMessageStore(IEmailProcessingService processingService, ILogger<CustomMessageStore> logger)
    {
        _processingService = processingService;
        _logger = logger;
    }

    public override async Task<SmtpResponse> SaveAsync(
        ISessionContext context,
        IMessageTransaction transaction,
        ReadOnlySequence<byte> buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            var recipients = transaction.To.Select(t => t.AsAddress()).ToList();
            var sender = transaction.From?.AsAddress() ?? string.Empty;

            using var memoryStream = new MemoryStream();
            foreach (var segment in buffer)
            {
                memoryStream.Write(segment.Span);
            }
            memoryStream.Position = 0;

            await _processingService.ProcessMessageStreamAsync(memoryStream, recipients, sender, cancellationToken);

            return SmtpResponse.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process inbound SMTP message");
            return new SmtpResponse(SmtpReplyCode.TransactionFailed, "Failed to process message");
        }
    }
}
