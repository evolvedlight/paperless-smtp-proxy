using System.Text;
using HomeSmtpServer.Hubs;
using HomeSmtpServer.Models;
using HomeSmtpServer.Services;
using Microsoft.AspNetCore.Mvc;
using MimeKit;

var builder = WebApplication.CreateBuilder(args);

// Configure Settings
builder.Services.Configure<SmtpServerSettings>(builder.Configuration.GetSection("SmtpServer"));
builder.Services.Configure<PaperlessSettings>(builder.Configuration.GetSection("Paperless"));

// Register Core Services
builder.Services.AddSingleton<IEmailRepository, InMemoryEmailRepository>();
builder.Services.AddSingleton<CustomMessageStore>();
builder.Services.AddSingleton<CustomMailboxFilter>();
builder.Services.AddSingleton<IEmailProcessingService, EmailProcessingService>();

// Register Paperless HttpClient & Service
builder.Services.AddHttpClient<IPaperlessService, PaperlessService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

// SignalR & Static Files
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

// Hosted SMTP Server Background Service
builder.Services.AddHostedService<SmtpHostedService>();

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// SignalR Hub Endpoint
app.MapHub<MailHub>("/mailhub");

// --- API Endpoints ---

// GET: List all emails
app.MapGet("/api/emails", (IEmailRepository repo) =>
{
    var emails = repo.GetAll().Select(EmailProcessingService.CreateEmailSummary);
    return Results.Ok(emails);
});

// GET: Get single email details
app.MapGet("/api/emails/{id}", (string id, IEmailRepository repo) =>
{
    var email = repo.GetById(id);
    if (email == null) return Results.NotFound(new { Message = "Email not found" });
    return Results.Ok(email);
});

// GET: Download attachment
app.MapGet("/api/emails/{emailId}/attachments/{attachmentId}", (string emailId, string attachmentId, IEmailRepository repo) =>
{
    var attachment = repo.GetAttachment(emailId, attachmentId);
    if (attachment == null) return Results.NotFound(new { Message = "Attachment not found" });

    return Results.File(
        fileContents: attachment.Data,
        contentType: attachment.ContentType,
        fileDownloadName: attachment.FileName
    );
});

// POST: Trigger Paperless upload for a single attachment
app.MapPost("/api/emails/{emailId}/attachments/{attachmentId}/paperless", async (
    string emailId, 
    string attachmentId, 
    IEmailRepository repo, 
    IPaperlessService paperlessService) =>
{
    var email = repo.GetById(emailId);
    if (email == null) return Results.NotFound(new { Message = "Email not found" });

    var attachment = email.Attachments.FirstOrDefault(a => a.Id == attachmentId);
    if (attachment == null) return Results.NotFound(new { Message = "Attachment not found" });

    var result = await paperlessService.UploadAttachmentAsync(email, attachment);
    return Results.Ok(result);
});

// POST: Trigger Paperless upload for all attachments in an email
app.MapPost("/api/emails/{emailId}/paperless", async (
    string emailId, 
    IEmailRepository repo, 
    IPaperlessService paperlessService) =>
{
    var email = repo.GetById(emailId);
    if (email == null) return Results.NotFound(new { Message = "Email not found" });

    var results = await paperlessService.ProcessEmailAttachmentsAsync(email);
    return Results.Ok(results);
});

// DELETE: Clear email history
app.MapDelete("/api/emails", (IEmailRepository repo) =>
{
    repo.Clear();
    return Results.Ok(new { Message = "Email history cleared" });
});

// GET: Get current system config & info
app.MapGet("/api/info", (IConfiguration config) =>
{
    var smtp = config.GetSection("SmtpServer").Get<SmtpServerSettings>() ?? new();
    var paperless = config.GetSection("Paperless").Get<PaperlessSettings>() ?? new();

    return Results.Ok(new
    {
        Smtp = new
        {
            smtp.Port,
            smtp.ServerName,
            smtp.AllowAnyRecipient,
            smtp.AllowedRecipients,
            smtp.AllowedDomains
        },
        Paperless = new
        {
            paperless.Enabled,
            paperless.BaseUrl,
            HasToken = !string.IsNullOrWhiteSpace(paperless.ApiToken),
            paperless.AutoUploadAttachments,
            paperless.TitleFormat
        },
        ServerTime = DateTimeOffset.UtcNow
    });
});

// POST: Send a simulated / test email
app.MapPost("/api/emails/test", async (
    [FromBody] TestEmailRequest request, 
    IEmailProcessingService processingService) =>
{
    var mimeMessage = new MimeMessage();
    mimeMessage.From.Add(MailboxAddress.Parse(request.From));
    mimeMessage.To.Add(MailboxAddress.Parse(request.To));
    mimeMessage.Subject = request.Subject;

    var builder = new BodyBuilder
    {
        TextBody = request.Body,
        HtmlBody = $"<div style='font-family: sans-serif; padding: 15px;'><h3>{request.Subject}</h3><p>{request.Body.Replace("\n", "<br/>")}</p></div>"
    };

    if (request.IncludeSamplePdf)
    {
        var samplePdfBytes = GenerateSamplePdf(request.Subject, request.From);
        builder.Attachments.Add($"Invoice_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf", samplePdfBytes, new ContentType("application", "pdf"));
    }

    mimeMessage.Body = builder.ToMessageBody();

    var email = await processingService.ProcessMimeMessageAsync(mimeMessage, new[] { request.To }, request.From);
    return Results.Ok(new { Message = "Test email generated and processed successfully", EmailId = email.Id });
});

app.Run();

// Helper to generate a minimal valid PDF byte array for testing
static byte[] GenerateSamplePdf(string title, string from)
{
    var content = $"""
%PDF-1.4
1 0 obj <</Type /Catalog /Pages 2 0 R>> endobj
2 0 obj <</Type /Pages /Kids [3 0 R] /Count 1>> endobj
3 0 obj <</Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj
4 0 obj <</Length 120>> stream
BT
/F1 18 Tf
50 720 Td
(Paperless Import Test Document) Tj
/F1 12 Tf
0 -30 Td
(Subject: {title}) Tj
0 -20 Td
(From: {from}) Tj
0 -20 Td
(Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC) Tj
ET
endstream
endobj
5 0 obj <</Type /Font /Subtype /Type1 /BaseFont /Helvetica>> endobj
xref
0 6
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000244 00000 n 
0000000415 00000 n 
trailer <</Size 6 /Root 1 0 R>>
startxref
492
%%EOF
""";

    return Encoding.ASCII.GetBytes(content);
}
