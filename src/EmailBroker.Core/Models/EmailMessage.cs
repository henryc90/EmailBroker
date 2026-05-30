namespace EmailBroker.Core.Models;

public class EmailMessage
{
    public string From { get; init; } = string.Empty;
    public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();
    public string Subject { get; init; } = string.Empty;
    public string? HtmlBody { get; init; }
    public string? TextBody { get; init; }
    public IReadOnlyList<string>? Cc { get; init; }
    public IReadOnlyList<string>? Bcc { get; init; }
    public IReadOnlyList<string>? ReplyTo { get; init; }
    public IReadOnlyList<EmailAttachment>? Attachments { get; init; }
    public IReadOnlyList<EmailTag>? Tags { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public DateTimeOffset? ScheduledAt { get; init; }
}
