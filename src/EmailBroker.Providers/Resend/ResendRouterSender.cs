using System.Runtime.CompilerServices;
using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using Microsoft.Extensions.Options;
using Resend;
using DomainMessage = EmailBroker.Core.Models.EmailMessage;

namespace EmailBroker.Providers.Resend;

public class ResendRouterSender : IEmailSender
{
    private readonly Dictionary<string, ResendEmailSender> _senders;
    private readonly ResendEmailSender? _defaultSender;

    public ResendRouterSender(IOptions<ResendOptions> options)
    {
        var opts = options.Value;
        _senders = new Dictionary<string, ResendEmailSender>(StringComparer.OrdinalIgnoreCase);

        // Build senders from multi-account config
        if (opts.Accounts is { Count: > 0 })
        {
            foreach (var account in opts.Accounts)
            {
                if (string.IsNullOrEmpty(account.Domain) || string.IsNullOrEmpty(account.ApiToken))
                    continue;

                var client = CreateClient(opts.ApiUrl, account.ApiToken);
                var senderOpts = Options.Create(new ResendOptions
                {
                    ApiUrl = opts.ApiUrl,
                    ApiToken = account.ApiToken
                });
                _senders[account.Domain] = new ResendEmailSender(senderOpts, client);
            }
        }

        // Fallback: single account from top-level ApiToken (backward compat)
        if (!string.IsNullOrEmpty(opts.ApiToken) && _senders.Count == 0)
        {
            var client = CreateClient(opts.ApiUrl, opts.ApiToken);
            _defaultSender = new ResendEmailSender(options, client);
        }
    }

    public Task<SendEmailResponse> SendAsync(DomainMessage message, CancellationToken cancellationToken = default)
    {
        var sender = ResolveSender(message.From);
        return sender.SendAsync(message, cancellationToken);
    }

    public async Task<IReadOnlyList<SendEmailResponse>> SendBatchAsync(
        IReadOnlyList<DomainMessage> messages,
        CancellationToken cancellationToken = default)
    {
        // Group messages by sender so each goes through its own account
        var groups = messages
            .Select(m => (Message: m, Sender: ResolveSender(m.From)))
            .GroupBy(x => x.Sender, ReferenceEqualityComparer.Instance);

        var results = new List<SendEmailResponse>(messages.Count);

        foreach (var group in groups)
        {
            var groupedMessages = group.Select(x => x.Message).ToList().AsReadOnly();
            var groupResult = await group.Key.SendBatchAsync(groupedMessages, cancellationToken);
            results.AddRange(groupResult);
        }

        return results.AsReadOnly();
    }

    private ResendEmailSender ResolveSender(string from)
    {
        var domain = ExtractDomain(from);

        if (domain is not null && _senders.TryGetValue(domain, out var sender))
            return sender;

        if (_defaultSender is not null)
            return _defaultSender;

        throw new InvalidOperationException(
            $"No Resend account configured for domain '{domain}'. " +
            "Add an account entry in Resend:Accounts or set a default ApiToken.");
    }

    private static string? ExtractDomain(string from)
    {
        if (string.IsNullOrEmpty(from))
            return null;

        var idx = from.LastIndexOf('@');
        return idx >= 0 ? from[(idx + 1)..] : null;
    }

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

    /// <summary>
    /// Reference equality comparer for grouping ResendEmailSender instances.
    /// Each sender is a singleton per domain, so reference equality is correct.
    /// </summary>
    private sealed class ReferenceEqualityComparer : IEqualityComparer<ResendEmailSender>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(ResendEmailSender? x, ResendEmailSender? y) => ReferenceEquals(x, y);
        public int GetHashCode(ResendEmailSender obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
