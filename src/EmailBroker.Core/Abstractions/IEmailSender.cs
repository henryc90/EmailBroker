using EmailBroker.Core.Models;

namespace EmailBroker.Core.Abstractions;

public interface IEmailSender
{
    Task<SendEmailResponse> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SendEmailResponse>> SendBatchAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default);
}
