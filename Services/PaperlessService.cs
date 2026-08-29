using System.Net.Http.Headers;
using HomeSmtpServer.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using HomeSmtpServer.Hubs;

namespace HomeSmtpServer.Services;

public interface IPaperlessService
{
    Task<PaperlessUploadResult> UploadAttachmentAsync(ReceivedEmail email, EmailAttachment attachment, CancellationToken cancellationToken = default);
    Task<List<PaperlessUploadResult>> ProcessEmailAttachmentsAsync(ReceivedEmail email, CancellationToken cancellationToken = default);
}

public class PaperlessUploadResult
{
    public bool Success { get; set; }
    public string AttachmentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? TaskId { get; set; }
    public string? Message { get; set; }
    public int? StatusCode { get; set; }
}

public class PaperlessService : IPaperlessService
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<PaperlessSettings> _settings;
    private readonly ILogger<PaperlessService> _logger;
    private readonly IHubContext<MailHub> _hubContext;

    public PaperlessService(
        HttpClient httpClient,
        IOptionsMonitor<PaperlessSettings> settings,
        ILogger<PaperlessService> logger,
        IHubContext<MailHub> hubContext)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task<List<PaperlessUploadResult>> ProcessEmailAttachmentsAsync(ReceivedEmail email, CancellationToken cancellationToken = default)
    {
        var results = new List<PaperlessUploadResult>();
        var config = _settings.CurrentValue;

        if (!config.Enabled)
        {
            _logger.LogInformation("Paperless upload skipped for email {EmailId}: Paperless integration is disabled.", email.Id);
            return results;
        }

        if (email.Attachments.Count == 0)
        {
            _logger.LogInformation("No attachments to upload for email {EmailId}.", email.Id);
            return results;
        }

        foreach (var attachment in email.Attachments)
        {
            var result = await UploadAttachmentAsync(email, attachment, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    public async Task<PaperlessUploadResult> UploadAttachmentAsync(ReceivedEmail email, EmailAttachment attachment, CancellationToken cancellationToken = default)
    {
        var config = _settings.CurrentValue;
        var result = new PaperlessUploadResult
        {
            AttachmentId = attachment.Id,
            FileName = attachment.FileName
        };

        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.ApiToken))
        {
            result.Success = false;
            result.Message = "Paperless BaseUrl or ApiToken is not configured.";
            _logger.LogWarning("Paperless upload failed: {Message}", result.Message);
            return result;
        }

        try
        {
            var baseUrl = config.BaseUrl.TrimEnd('/');
            var endpoint = $"{baseUrl}/api/documents/post_document/";

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", config.ApiToken.Trim());

            using var content = new MultipartFormDataContent();

            // Document file content
            var fileContent = new ByteArrayContent(attachment.Data);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType);
            content.Add(fileContent, "document", attachment.FileName);

            // Document Title
            var title = config.TitleFormat
                .Replace("{Subject}", email.Subject)
                .Replace("{FileName}", attachment.FileName)
                .Replace("{From}", email.From);
            content.Add(new StringContent(title), "title");

            // Document created timestamp
            content.Add(new StringContent(email.ReceivedAt.ToString("o")), "created");

            request.Content = content;

            _logger.LogInformation("Uploading attachment {FileName} ({Size} bytes) to Paperless at {Endpoint}...", 
                attachment.FileName, attachment.Size, endpoint);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            result.StatusCode = (int)response.StatusCode;

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                result.Success = true;
                result.TaskId = responseBody.Trim('"');
                result.Message = $"Uploaded successfully (Task: {result.TaskId})";
                _logger.LogInformation("Successfully uploaded {FileName} to Paperless: {ResponseBody}", attachment.FileName, responseBody);
            }
            else
            {
                result.Success = false;
                result.Message = $"Paperless returned HTTP {response.StatusCode}: {responseBody}";
                _logger.LogError("Failed uploading {FileName} to Paperless. HTTP {StatusCode}: {ResponseBody}", 
                    attachment.FileName, response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Error uploading to Paperless: {ex.Message}";
            _logger.LogError(ex, "Exception while uploading attachment {FileName} to Paperless", attachment.FileName);
        }

        // Notify SignalR clients about status update
        await _hubContext.Clients.All.SendAsync("PaperlessStatusUpdated", new
        {
            EmailId = email.Id,
            AttachmentId = attachment.Id,
            FileName = attachment.FileName,
            Success = result.Success,
            Message = result.Message
        }, cancellationToken);

        return result;
    }
}
