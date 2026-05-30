namespace EmailBroker.Core.Models;

public class SendEmailResponse
{
    public bool Success { get; init; }
    public string? MessageId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }
}
