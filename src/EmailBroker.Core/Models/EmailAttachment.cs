namespace EmailBroker.Core.Models;

public class EmailAttachment
{
    public string Filename { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty; // Base64 encoded
    public string? ContentType { get; init; }
    public string? ContentId { get; init; }
}
