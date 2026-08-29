using Microsoft.AspNetCore.SignalR;
using HomeSmtpServer.Models;

namespace HomeSmtpServer.Hubs;

public class MailHub : Hub
{
    public async Task SendTestNotification(string message)
    {
        await Clients.All.SendAsync("ServerLog", new
        {
            Level = "Info",
            Message = message,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
