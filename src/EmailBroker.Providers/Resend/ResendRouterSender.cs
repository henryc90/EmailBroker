using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using Microsoft.Extensions.Options;
using Resend;
using DomainMessage = EmailBroker.Core.Models.EmailMessage;

namespace EmailBroker.Providers.Resend;

public class ResendRouterSender : IEmailSender
{
    private readonly ResendEmailSender _sender;

    public ResendRouterSender(IOptions<ResendOptions> options)
    {
        var opts = options.Value;
        var client = CreateClient(opts.ApiUrl, opts.ApiToken);
        _sender = new ResendEmailSender(options, client);
    }

    public Task<SendEmailResponse> SendAsync(DomainMessage message, CancellationToken cancellationToken = default)
        => _sender.SendAsync(message, cancellationToken);

    public Task<IReadOnlyList<SendEmailResponse>> SendBatchAsync(
        IReadOnlyList<DomainMessage> messages,
        CancellationToken cancellationToken = default)
        => _sender.SendBatchAsync(messages, cancellationToken);

    private static IResend CreateClient(string apiUrl, string apiToken)
    {
        var clientOptions = new ResendClientOptions
        {
            ApiToken = apiToken,
            ApiUrl = apiUrl,
            ThrowExceptions = true
        };
        return ResendClient.Create(clientOptions, new HttpClient());
    }
}
