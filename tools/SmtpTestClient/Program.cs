using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace SmtpTestClient;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Parse command-line arguments
        var options = ParseArguments(args);

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        PrintBanner();

        // If no specific action/args were provided, offer interactive menu
        if (args.Length == 0)
        {
            return await RunInteractiveMenuAsync();
        }

        return await SendEmailAsync(options);
    }

    static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("  📬 Paperless SMTP Test Client");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
    }

    static void PrintHelp()
    {
        PrintBanner();
        Console.WriteLine("Usage: dotnet run --project tools/SmtpTestClient -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --host <host>       Target SMTP server host (default: localhost)");
        Console.WriteLine("  -p, --port <port>       Target SMTP server port (default: 2525)");
        Console.WriteLine("  -t, --to <email>        Recipient address (default: steve@paperless.brown.bg)");
        Console.WriteLine("  -f, --from <email>      Sender address (default: billing@utility-provider.com)");
        Console.WriteLine("  -s, --subject <text>    Email subject line");
        Console.WriteLine("  -b, --body <text>       Email message body text");
        Console.WriteLine("  -a, --attach <filepath> Attach a local file (e.g. -a C:\\scan.pdf)");
        Console.WriteLine("      --pdf               Generate and attach a sample invoice PDF");
        Console.WriteLine("      --count <num>       Number of emails to send (default: 1)");
        Console.WriteLine("  -?, --help              Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  # Send quick test email with sample PDF to local dev server (port 2525)");
        Console.WriteLine("  dotnet run --project tools/SmtpTestClient -- --pdf");
        Console.WriteLine();
        Console.WriteLine("  # Send email to your NAS on port 25");
        Console.WriteLine("  dotnet run --project tools/SmtpTestClient -- -h 192.168.1.100 -p 25 --pdf");
        Console.WriteLine();
        Console.WriteLine("  # Send email with a real local PDF attachment");
        Console.WriteLine("  dotnet run --project tools/SmtpTestClient -- -a \"D:\\scans\\receipt.pdf\" -s \"Hardware Receipt\"");
        Console.WriteLine();
    }

    static async Task<int> RunInteractiveMenuAsync()
    {
        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Select an action:");
            Console.ResetColor();
            Console.WriteLine("  [1] Quick Send: Test Invoice with Sample PDF (to localhost:2525)");
            Console.WriteLine("  [2] Quick Send: Plain Text Email (no attachment)");
            Console.WriteLine("  [3] Send Real Local File Attachment");
            Console.WriteLine("  [4] Send to NAS / Remote Host (custom Host & Port)");
            Console.WriteLine("  [5] Bulk Test (send 5 emails in a stream)");
            Console.WriteLine("  [0] Exit");
            Console.Write("\nEnter choice [1-5, 0]: ");

            var key = Console.ReadLine()?.Trim();
            if (key == "0") return 0;

            var options = new SmtpOptions();

            switch (key)
            {
                case "1":
                    options.IncludeSamplePdf = true;
                    options.Subject = $"Monthly Power Bill #{Random.Shared.Next(10000, 99999)}";
                    await SendEmailAsync(options);
                    break;

                case "2":
                    options.IncludeSamplePdf = false;
                    options.Subject = $"Server Status Notification #{Random.Shared.Next(100, 999)}";
                    options.Body = "Hello Steve,\n\nThis is a plain test email with no attachments.\n\nAll services operational.";
                    await SendEmailAsync(options);
                    break;

                case "3":
                    Console.Write("Enter path to file: ");
                    var path = Console.ReadLine()?.Trim('"', ' ');
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"File not found: '{path}'");
                        Console.ResetColor();
                        break;
                    }
                    options.AttachmentPath = path;
                    options.Subject = $"Document: {Path.GetFileName(path)}";
                    await SendEmailAsync(options);
                    break;

                case "4":
                    Console.Write("Target Host (e.g. 192.168.1.100 or localhost) [localhost]: ");
                    var h = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrWhiteSpace(h)) options.Host = h;

                    Console.Write("Target Port [25]: ");
                    var pStr = Console.ReadLine()?.Trim();
                    if (int.TryParse(pStr, out var p)) options.Port = p;
                    else options.Port = 25;

                    Console.Write("Recipient Email [steve@paperless.brown.bg]: ");
                    var to = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrWhiteSpace(to)) options.To = to;

                    options.IncludeSamplePdf = true;
                    options.Subject = $"Remote Test Bill #{Random.Shared.Next(10000, 99999)}";
                    await SendEmailAsync(options);
                    break;

                case "5":
                    Console.Write("How many emails to send [5]: ");
                    var countStr = Console.ReadLine()?.Trim();
                    var count = int.TryParse(countStr, out var c) ? c : 5;
                    for (int i = 1; i <= count; i++)
                    {
                        var burstOpt = new SmtpOptions
                        {
                            Host = options.Host,
                            Port = options.Port,
                            Subject = $"Bulk Document Batch #{i} of {count} - Ref {Random.Shared.Next(1000, 9999)}",
                            IncludeSamplePdf = true
                        };
                        Console.WriteLine($"\n--- Sending email {i} of {count} ---");
                        await SendEmailAsync(burstOpt);
                        await Task.Delay(500);
                    }
                    break;

                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }
        }
    }

    static async Task<int> SendEmailAsync(SmtpOptions options)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(options.From));
            message.To.Add(MailboxAddress.Parse(options.To));
            message.Subject = options.Subject;

            var bodyBuilder = new BodyBuilder
            {
                TextBody = options.Body,
                HtmlBody = $"""
                <div style="font-family: Arial, sans-serif; padding: 15px; border: 1px solid #e2e8f0; border-radius: 8px;">
                    <h2 style="color: #0284c7;">{options.Subject}</h2>
                    <p style="white-space: pre-line; line-height: 1.5;">{options.Body}</p>
                    <hr style="border: none; border-top: 1px solid #e2e8f0; margin: 15px 0;" />
                    <small style="color: #64748b;">Automated message sent by Paperless SMTP Test Client at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</small>
                </div>
                """
            };

            // Custom file attachment
            if (!string.IsNullOrWhiteSpace(options.AttachmentPath) && File.Exists(options.AttachmentPath))
            {
                Console.WriteLine($"📎 Attaching file: {options.AttachmentPath}");
                await bodyBuilder.Attachments.AddAsync(options.AttachmentPath);
            }

            // Generated sample PDF attachment
            if (options.IncludeSamplePdf)
            {
                var pdfBytes = GenerateSamplePdf(options.Subject, options.From, options.To);
                var pdfName = $"Invoice_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
                Console.WriteLine($"📄 Generating sample PDF attachment: {pdfName} ({pdfBytes.Length} bytes)");
                bodyBuilder.Attachments.Add(pdfName, pdfBytes, new ContentType("application", "pdf"));
            }

            message.Body = bodyBuilder.ToMessageBody();

            Console.WriteLine();
            Console.WriteLine($"Connecting to SMTP server at {options.Host}:{options.Port}...");

            using var client = new SmtpClient();
            
            // Connect with no TLS requirement for local / internal relay
            await client.ConnectAsync(options.Host, options.Port, SecureSocketOptions.None);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Connected to {options.Host}:{options.Port}");
            Console.ResetColor();

            Console.WriteLine($"Sending message: From='{options.From}' -> To='{options.To}' | Subject='{options.Subject}'...");
            
            var startTime = DateTime.UtcNow;
            var response = await client.SendAsync(message);
            var elapsed = DateTime.UtcNow - startTime;

            await client.DisconnectAsync(true);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"🎉 Email delivered successfully in {elapsed.TotalMilliseconds:0}ms!");
            Console.WriteLine($"   Server Response: {response}");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Failed to deliver email to {options.Host}:{options.Port}");
            Console.WriteLine($"   Error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    static SmtpOptions ParseArguments(string[] args)
    {
        var options = new SmtpOptions();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--host":
                    if (i + 1 < args.Length) options.Host = args[++i];
                    break;
                case "-p":
                case "--port":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var p)) options.Port = p;
                    break;
                case "-t":
                case "--to":
                    if (i + 1 < args.Length) options.To = args[++i];
                    break;
                case "-f":
                case "--from":
                    if (i + 1 < args.Length) options.From = args[++i];
                    break;
                case "-s":
                case "--subject":
                    if (i + 1 < args.Length) options.Subject = args[++i];
                    break;
                case "-b":
                case "--body":
                    if (i + 1 < args.Length) options.Body = args[++i];
                    break;
                case "-a":
                case "--attach":
                    if (i + 1 < args.Length) options.AttachmentPath = args[++i];
                    break;
                case "--pdf":
                    options.IncludeSamplePdf = true;
                    break;
                case "-?":
                case "--help":
                    options.ShowHelp = true;
                    break;
            }
        }

        return options;
    }

    static byte[] GenerateSamplePdf(string subject, string from, string to)
    {
        var content = $"""
%PDF-1.4
1 0 obj <</Type /Catalog /Pages 2 0 R>> endobj
2 0 obj <</Type /Pages /Kids [3 0 R] /Count 1>> endobj
3 0 obj <</Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj
4 0 obj <</Length 180>> stream
BT
/F1 20 Tf
50 720 Td
(PAPERLESS INVOICE STATEMENT) Tj
/F1 12 Tf
0 -35 Td
(Subject: {subject}) Tj
0 -20 Td
(Sender: {from}) Tj
0 -20 Td
(Recipient: {to}) Tj
0 -20 Td
(Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC) Tj
0 -30 Td
(Total Amount: $249.00 USD - PAID) Tj
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
0000000475 00000 n 
trailer <</Size 6 /Root 1 0 R>>
startxref
552
%%EOF
""";
        return Encoding.ASCII.GetBytes(content);
    }
}

class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 2525;
    public string To { get; set; } = "steve@paperless.brown.bg";
    public string From { get; set; } = "billing@electric-company.com";
    public string Subject { get; set; } = "Monthly Utility Bill #88412";
    public string Body { get; set; } = "Hello Steve,\n\nPlease find attached your monthly invoice statement.\n\nThank you!";
    public string? AttachmentPath { get; set; }
    public bool IncludeSamplePdf { get; set; } = false;
    public bool ShowHelp { get; set; } = false;
}
