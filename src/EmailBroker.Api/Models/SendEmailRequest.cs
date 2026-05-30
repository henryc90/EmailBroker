namespace EmailBroker.Api.Models;

public class SendEmailRequest
{
    public string From { get; set; } = string.Empty;
    public List<string> To { get; set; } = [];
    public string Subject { get; set; } = string.Empty;
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }
    public List<string>? Cc { get; set; }
    public List<string>? Bcc { get; set; }
    public List<string>? ReplyTo { get; set; }
    public List<EmailAttachmentRequest>? Attachments { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
}

public class EmailAttachmentRequest
{
    public string Filename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty; // Base64-encoded content
    public string? ContentType { get; set; }
    public string? ContentId { get; set; }
}
