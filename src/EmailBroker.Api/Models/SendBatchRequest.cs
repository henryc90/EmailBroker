namespace EmailBroker.Api.Models;

public class SendBatchRequest
{
    public List<SendEmailRequest> Messages { get; set; } = [];
}
