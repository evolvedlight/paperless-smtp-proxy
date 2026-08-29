namespace HomeSmtpServer.Models;

public class EmailAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public string FormattedSize => FormatSize(Size);

    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {suffixes[order]}";
    }
}

public class ReceivedEmail
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string From { get; set; } = string.Empty;
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public string Subject { get; set; } = "(No Subject)";
    public string? TextBody { get; set; }
    public string? HtmlBody { get; set; }
    public List<EmailAttachment> Attachments { get; set; } = new();
    public long RawSize { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Paperless integration tracking
    public string PaperlessStatus { get; set; } = "Not Processed"; // "Not Processed", "Uploaded", "Failed", "No Attachments"
    public string? PaperlessMessage { get; set; }
    public DateTimeOffset? PaperlessProcessedAt { get; set; }
}

public class SmtpServerSettings
{
    public int Port { get; set; } = 25;
    public string ServerName { get; set; } = "paperless.brown.bg";
    public bool AllowAnyRecipient { get; set; } = true;
    public List<string> AllowedRecipients { get; set; } = new() { "steve@paperless.brown.bg" };
    public List<string> AllowedDomains { get; set; } = new() { "paperless.brown.bg" };
}

public class PaperlessSettings
{
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public string ApiToken { get; set; } = "";
    public bool AutoUploadAttachments { get; set; } = true;
    public string TitleFormat { get; set; } = "{Subject} - {FileName}";
    public List<string> DefaultTags { get; set; } = new() { "email-import" };
    public string? DocumentType { get; set; }
    public string? Correspondent { get; set; }
}

public class TestEmailRequest
{
    public string From { get; set; } = "sender@example.com";
    public string To { get; set; } = "steve@paperless.brown.bg";
    public string Subject { get; set; } = "Invoice #1024 from Supplier";
    public string Body { get; set; } = "Hi Steve,\n\nPlease find attached the monthly invoice.\n\nBest regards,\nSupplier";
    public bool IncludeSamplePdf { get; set; } = true;
}
