using System.Buffers;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using HomeSmtpServer.Services;
using SmtpServer.Mail;
using HomeSmtpServer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace HomeSmtpServer.Services;

public class CustomMessageStore : MessageStore
{
    private readonly IEmailProcessingService _processingService;
    private readonly ILogger<CustomMessageStore> _logger;
    private readonly IHubContext<MailHub> _hubContext;

    public CustomMessageStore(
        IEmailProcessingService processingService, 
        ILogger<CustomMessageStore> logger,
        IHubContext<MailHub> hubContext)
    {
        _processingService = processingService;
        _logger = logger;
        _hubContext = hubContext;
    }

    public override async Task<SmtpResponse> SaveAsync(
        ISessionContext context,
        IMessageTransaction transaction,
        ReadOnlySequence<byte> buffer,
        CancellationToken cancellationToken)
    {
        var localPort = context.EndpointDefinition?.Endpoint?.Port.ToString() ?? "25";
        var recipients = transaction.To.Select(t => t.AsAddress()).ToList();
        var sender = transaction.From?.AsAddress() ?? string.Empty;

        _logger.LogInformation("📦 [SMTP DATA] Received {Length} bytes on Port {Port} (Sender: <{Sender}>, Recipients: [{Recipients}])",
            buffer.Length, localPort, sender, string.Join(", ", recipients));

        _ = _hubContext.Clients.All.SendAsync("ServerLog", new
        {
            Level = "Info",
            Message = $"[SMTP DATA] Received {buffer.Length} bytes on Port :{localPort} (To: {string.Join(", ", recipients)})",
            Timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);

        try
        {
            using var memoryStream = new MemoryStream();
            foreach (var segment in buffer)
            {
                memoryStream.Write(segment.Span);
            }
            memoryStream.Position = 0;

            var email = await _processingService.ProcessMessageStreamAsync(memoryStream, recipients, sender, cancellationToken);

            _logger.LogInformation("✅ [EMAIL STORED] Message ID='{EmailId}', Subject='{Subject}', Attachments={Count}",
                email.Id, email.Subject, email.Attachments.Count);

            return SmtpResponse.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [SMTP ERROR] Failed to process inbound SMTP message on Port {Port}", localPort);

            _ = _hubContext.Clients.All.SendAsync("ServerLog", new
            {
                Level = "Error",
                Message = $"[SMTP Error] Failed to parse message on Port :{localPort}: {ex.Message}",
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);

            return new SmtpResponse(SmtpReplyCode.TransactionFailed, "Failed to process message");
        }
    }
}
