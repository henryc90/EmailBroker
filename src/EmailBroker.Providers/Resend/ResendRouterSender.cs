using System.Runtime.CompilerServices;
using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;
using DomainMessage = EmailBroker.Core.Models.EmailMessage;

namespace EmailBroker.Providers.Resend;

public class ResendRouterSender : IEmailSender
{
    private readonly Dictionary<string, ResendEmailSender> _senders;
    private readonly ResendEmailSender? _defaultSender;
    private readonly ILogger<ResendRouterSender> _logger;

    public ResendRouterSender(IOptions<ResendOptions> options, ILogger<ResendRouterSender> logger)
    {
        _logger = logger;
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

                var truncated = account.ApiToken.Length > 6
                    ? account.ApiToken[..(account.ApiToken.IndexOf('_') >= 0 ? account.ApiToken.IndexOf('_') + 4 : 6)] + "***"
                    : "***";
                _logger.LogInformation(
                    "Resend sender registered — Domain: {Domain}, Token: {TruncatedToken}",
                    account.Domain, truncated);
            }
        }

        // Fallback: single account from top-level ApiToken (backward compat)
        if (!string.IsNullOrEmpty(opts.ApiToken) && _senders.Count == 0)
        {
            var client = CreateClient(opts.ApiUrl, opts.ApiToken);
            _defaultSender = new ResendEmailSender(options, client);
            _logger.LogInformation("Resend sender registered — default (flat ApiToken)");
        }

        if (_senders.Count == 0 && _defaultSender is null)
        {
            _logger.LogWarning("Resend is not configured — no senders registered");
        }
        else
        {
            _logger.LogInformation(
                "Resend total senders: {Count} domain-specific, default: {HasDefault}",
                _senders.Count, _defaultSender is not null);
        }
    }

    public async Task<SendEmailResponse> SendAsync(DomainMessage message, CancellationToken cancellationToken = default)
    {
        var (sender, domain) = ResolveSender(message.From);
        _logger.LogInformation(
            "Resend sending email — Domain: {Domain}, From: {From}, To: {To}, Subject: {Subject}",
            domain, message.From, string.Join(",", message.To), message.Subject);
        var result = await sender.SendAsync(message, cancellationToken);
        _logger.LogInformation(
            "Resend send result — Success: {Success}, MessageId: {MessageId}, Error: {Error}",
            result.Success, result.MessageId, result.ErrorMessage);
        return result;
    }

    public async Task<IReadOnlyList<SendEmailResponse>> SendBatchAsync(
        IReadOnlyList<DomainMessage> messages,
        CancellationToken cancellationToken = default)
    {
        // Group messages by sender so each goes through its own account
        var groups = messages
            .Select(m => (Message: m, Sender: ResolveSender(m.From)))
            .GroupBy(x => x.Sender.Sender, ReferenceEqualityComparer.Instance);

        var results = new List<SendEmailResponse>(messages.Count);

        foreach (var group in groups)
        {
            var groupedMessages = group.Select(x => x.Message).ToList().AsReadOnly();
            var groupResult = await group.Key.SendBatchAsync(groupedMessages, cancellationToken);
            results.AddRange(groupResult);
        }

        return results.AsReadOnly();
    }

    private (ResendEmailSender Sender, string? Domain) ResolveSender(string from)
    {
        var domain = ExtractDomain(from);

        if (domain is not null && _senders.TryGetValue(domain, out var sender))
            return (sender, domain);

        if (_defaultSender is not null)
            return (_defaultSender, domain ?? "unknown");

        throw new InvalidOperationException(
            $"No Resend account configured for domain '{domain}'. " +
            "Add an account entry in Resend:Accounts or set a default ApiToken.");
    }

    private static string? ExtractDomain(string from)
    {
        if (string.IsNullOrEmpty(from))
            return null;

        // Support both "email@domain.com" and "Display Name <email@domain.com>"
        var idx = from.LastIndexOf('@');
        if (idx < 0)
            return null;

        var domain = from[(idx + 1)..].TrimEnd('>', ' ', ')');
        return domain;
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
