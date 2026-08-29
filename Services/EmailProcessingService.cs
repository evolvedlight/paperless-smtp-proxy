using MimeKit;
using HomeSmtpServer.Models;
using Microsoft.AspNetCore.SignalR;
using HomeSmtpServer.Hubs;
using Microsoft.Extensions.Options;

namespace HomeSmtpServer.Services;

public interface IEmailProcessingService
{
    Task<ReceivedEmail> ProcessMessageStreamAsync(Stream stream, IReadOnlyList<string> envelopeRecipients, string? envelopeSender = null, CancellationToken cancellationToken = default);
    Task<ReceivedEmail> ProcessMimeMessageAsync(MimeMessage message, IReadOnlyList<string>? envelopeRecipients = null, string? envelopeSender = null, CancellationToken cancellationToken = default);
}

public class EmailProcessingService : IEmailProcessingService
{
    private readonly IEmailRepository _repository;
    private readonly IHubContext<MailHub> _hubContext;
    private readonly IPaperlessService _paperlessService;
    private readonly IOptionsMonitor<PaperlessSettings> _paperlessSettings;
    private readonly ILogger<EmailProcessingService> _logger;

    public EmailProcessingService(
        IEmailRepository repository,
        IHubContext<MailHub> hubContext,
        IPaperlessService paperlessService,
        IOptionsMonitor<PaperlessSettings> paperlessSettings,
        ILogger<EmailProcessingService> logger)
    {
        _repository = repository;
        _hubContext = hubContext;
        _paperlessService = paperlessService;
        _paperlessSettings = paperlessSettings;
        _logger = logger;
    }

    public async Task<ReceivedEmail> ProcessMessageStreamAsync(
        Stream stream,
        IReadOnlyList<string> envelopeRecipients,
        string? envelopeSender = null,
        CancellationToken cancellationToken = default)
    {
        var rawSize = stream.CanSeek ? stream.Length : 0;
        var mimeMessage = await MimeMessage.LoadAsync(stream, cancellationToken);
        var email = await ProcessMimeMessageAsync(mimeMessage, envelopeRecipients, envelopeSender, cancellationToken);
        if (rawSize > 0)
        {
            email.RawSize = rawSize;
        }
        return email;
    }

    public async Task<ReceivedEmail> ProcessMimeMessageAsync(
        MimeMessage message,
        IReadOnlyList<string>? envelopeRecipients = null,
        string? envelopeSender = null,
        CancellationToken cancellationToken = default)
    {
        var email = new ReceivedEmail
        {
            ReceivedAt = DateTimeOffset.UtcNow,
            Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(No Subject)" : message.Subject,
            From = envelopeSender ?? message.From.ToString(),
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody
        };

        // Recipients
        if (envelopeRecipients != null && envelopeRecipients.Count > 0)
        {
            email.To.AddRange(envelopeRecipients);
        }
        else
        {
            foreach (var to in message.To.Mailboxes)
            {
                email.To.Add(to.Address);
            }
        }

        foreach (var cc in message.Cc.Mailboxes)
        {
            email.Cc.Add(cc.Address);
        }

        // Headers
        foreach (var header in message.Headers)
        {
            if (!email.Headers.ContainsKey(header.Field))
            {
                email.Headers[header.Field] = header.Value;
            }
        }

        // Attachments
        foreach (var attachmentPart in message.Attachments)
        {
            var fileName = attachmentPart.ContentType.Name ?? "attachment.bin";
            var contentType = attachmentPart.ContentType.MimeType;

            if (attachmentPart is MimePart mimePart)
            {
                if (!string.IsNullOrWhiteSpace(mimePart.FileName))
                {
                    fileName = mimePart.FileName;
                }

                byte[] data = Array.Empty<byte>();
                if (mimePart.Content != null)
                {
                    using var ms = new MemoryStream();
                    await mimePart.Content.DecodeToAsync(ms, cancellationToken);
                    data = ms.ToArray();
                }

                email.Attachments.Add(new EmailAttachment
                {
                    FileName = fileName,
                    ContentType = contentType,
                    Size = data.Length,
                    Data = data
                });
            }
            else if (attachmentPart is MessagePart messagePart)
            {
                byte[] data = Array.Empty<byte>();
                if (messagePart.Message != null)
                {
                    using var ms = new MemoryStream();
                    await messagePart.Message.WriteToAsync(ms, cancellationToken);
                    data = ms.ToArray();
                }

                email.Attachments.Add(new EmailAttachment
                {
                    FileName = messagePart.ContentType?.Name ?? "attached_message.eml",
                    ContentType = "message/rfc822",
                    Size = data.Length,
                    Data = data
                });
            }
        }

        _logger.LogInformation("Received Email: From='{From}', To='{To}', Subject='{Subject}', Attachments={Count}",
            email.From, string.Join(", ", email.To), email.Subject, email.Attachments.Count);

        // Check paperless status
        if (email.Attachments.Count == 0)
        {
            email.PaperlessStatus = "No Attachments";
        }
        else if (_paperlessSettings.CurrentValue.Enabled && _paperlessSettings.CurrentValue.AutoUploadAttachments)
        {
            email.PaperlessStatus = "Uploading...";
        }
        else
        {
            email.PaperlessStatus = "Ready (Manual Upload)";
        }

        // Store email
        _repository.AddEmail(email);

        // Broadcast to SignalR connected UI clients (without sending huge raw bytes in the notification payload)
        var summary = CreateEmailSummary(email);
        await _hubContext.Clients.All.SendAsync("EmailReceived", summary, cancellationToken);

        // Auto-upload to Paperless if enabled
        if (email.Attachments.Count > 0 && _paperlessSettings.CurrentValue.Enabled && _paperlessSettings.CurrentValue.AutoUploadAttachments)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var uploadResults = await _paperlessService.ProcessEmailAttachmentsAsync(email, CancellationToken.None);
                    var allSuccess = uploadResults.Count > 0 && uploadResults.All(r => r.Success);
                    var anySuccess = uploadResults.Any(r => r.Success);

                    email.PaperlessProcessedAt = DateTimeOffset.UtcNow;
                    email.PaperlessStatus = allSuccess ? "Uploaded" : (anySuccess ? "Partially Uploaded" : "Failed");
                    email.PaperlessMessage = string.Join("; ", uploadResults.Select(r => $"{r.FileName}: {r.Message}"));

                    _repository.Update(email);

                    await _hubContext.Clients.All.SendAsync("EmailUpdated", CreateEmailSummary(email));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in background Paperless upload for email {EmailId}", email.Id);
                    email.PaperlessStatus = "Error";
                    email.PaperlessMessage = ex.Message;
                    _repository.Update(email);
                    await _hubContext.Clients.All.SendAsync("EmailUpdated", CreateEmailSummary(email));
                }
            });
        }

        return email;
    }

    public static object CreateEmailSummary(ReceivedEmail email)
    {
        return new
        {
            email.Id,
            email.ReceivedAt,
            email.From,
            email.To,
            email.Cc,
            email.Subject,
            email.TextBody,
            email.HtmlBody,
            Attachments = email.Attachments.Select(a => new
            {
                a.Id,
                a.FileName,
                a.ContentType,
                a.Size,
                a.FormattedSize
            }).ToList(),
            email.RawSize,
            email.Headers,
            email.PaperlessStatus,
            email.PaperlessMessage,
            email.PaperlessProcessedAt
        };
    }
}
