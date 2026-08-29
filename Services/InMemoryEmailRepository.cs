using System.Collections.Concurrent;
using HomeSmtpServer.Models;

namespace HomeSmtpServer.Services;

public interface IEmailRepository
{
    void AddEmail(ReceivedEmail email);
    IReadOnlyList<ReceivedEmail> GetAll();
    ReceivedEmail? GetById(string id);
    EmailAttachment? GetAttachment(string emailId, string attachmentId);
    void Update(ReceivedEmail email);
    void Clear();
}

public class InMemoryEmailRepository : IEmailRepository
{
    private readonly ConcurrentDictionary<string, ReceivedEmail> _emails = new();
    private readonly ConcurrentQueue<string> _orderedIds = new();
    private readonly int _maxHistory = 100;

    public void AddEmail(ReceivedEmail email)
    {
        _emails[email.Id] = email;
        _orderedIds.Enqueue(email.Id);

        while (_orderedIds.Count > _maxHistory && _orderedIds.TryDequeue(out var oldId))
        {
            _emails.TryRemove(oldId, out _);
        }
    }

    public IReadOnlyList<ReceivedEmail> GetAll()
    {
        return _emails.Values
            .OrderByDescending(e => e.ReceivedAt)
            .ToList();
    }

    public ReceivedEmail? GetById(string id)
    {
        _emails.TryGetValue(id, out var email);
        return email;
    }

    public EmailAttachment? GetAttachment(string emailId, string attachmentId)
    {
        if (_emails.TryGetValue(emailId, out var email))
        {
            return email.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        }
        return null;
    }

    public void Update(ReceivedEmail email)
    {
        _emails[email.Id] = email;
    }

    public void Clear()
    {
        _emails.Clear();
        while (_orderedIds.TryDequeue(out _)) { }
    }
}
