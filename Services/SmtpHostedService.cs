using SmtpServer;
using SmtpServer.Storage;
using Microsoft.Extensions.Options;
using HomeSmtpServer.Models;
using HomeSmtpServer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace HomeSmtpServer.Services;

public class SmtpHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<SmtpServerSettings> _settings;
    private readonly ILogger<SmtpHostedService> _logger;
    private readonly IHubContext<MailHub> _hubContext;

    public SmtpHostedService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<SmtpServerSettings> settings,
        ILogger<SmtpHostedService> logger,
        IHubContext<MailHub> hubContext)
    {
        _serviceProvider = serviceProvider;
        _settings = settings;
        _logger = logger;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = _settings.CurrentValue;

        var options = new SmtpServerOptionsBuilder()
            .ServerName(config.ServerName)
            .Port(config.Port)
            .Build();

        var smtpServer = new SmtpServer.SmtpServer(options, _serviceProvider);

        _logger.LogInformation("==================================================");
        _logger.LogInformation("🚀 SMTP Server starting on port {Port} (ServerName: {ServerName})", config.Port, config.ServerName);
        _logger.LogInformation("==================================================");

        try
        {
            await smtpServer.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("SMTP Server stopped gracefully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP Server encountered an unexpected error.");
        }
    }
}
