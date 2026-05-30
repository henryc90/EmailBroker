using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using Microsoft.Extensions.Options;
using Resend;
using Attachment = Resend.EmailAttachment;
using DomainMessage = EmailBroker.Core.Models.EmailMessage;
using ResendMessage = Resend.EmailMessage;
using ResendTag = Resend.EmailTag;

namespace EmailBroker.Providers.Resend;

public class ResendEmailSender : IEmailSender
{
    private readonly IResend _resend;
    private readonly ResendOptions _options;

    public ResendEmailSender(IOptions<ResendOptions> options)
    {
        _options = options.Value;
        _resend = CreateResendClient(_options);
    }

    // Internal for testing — allows injecting a mock IResend
    internal ResendEmailSender(IOptions<ResendOptions> options, IResend resend)
    {
        _options = options.Value;
        _resend = resend;
    }

    private static IResend CreateResendClient(ResendOptions options)
    {
        var clientOptions = new ResendClientOptions
        {
            ApiToken = options.ApiToken,
            ApiUrl = options.ApiUrl,
            ThrowExceptions = true
        };
        return ResendClient.Create(clientOptions, new HttpClient());
    }

    public async Task<SendEmailResponse> SendAsync(DomainMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var resendMessage = MapToResendMessage(message);
            var response = await _resend.EmailSendAsync(resendMessage, cancellationToken);

            return new SendEmailResponse
            {
                Success = true,
                MessageId = response.Content.ToString(),
                Provider = "resend"
            };
        }
        catch (ResendException ex)
        {
            return new SendEmailResponse
            {
                Success = false,
                Provider = "resend",
                ErrorMessage = ex.Message,
                ErrorType = ClassifyError(ex)
            };
        }
    }

    public async Task<IReadOnlyList<SendEmailResponse>> SendBatchAsync(IReadOnlyList<DomainMessage> messages, CancellationToken cancellationToken = default)
    {
        var results = new List<SendEmailResponse>(messages.Count);

        try
        {
            var resendMessages = messages.Select(MapToResendMessage).ToList();
            var response = await _resend.EmailBatchAsync(resendMessages, cancellationToken);

            if (response?.Content is { Count: > 0 })
            {
                foreach (var id in response.Content)
                {
                    results.Add(new SendEmailResponse
                    {
                        Success = true,
                        MessageId = id.ToString(),
                        Provider = "resend"
                    });
                }
            }
        }
        catch (ResendException ex)
        {
            // If the entire batch fails, mark all as failed
            for (var i = results.Count; i < messages.Count; i++)
            {
                results.Add(new SendEmailResponse
                {
                    Success = false,
                    Provider = "resend",
                    ErrorMessage = ex.Message,
                    ErrorType = ClassifyError(ex)
                });
            }
        }

        // Fill remaining if response had fewer items than messages
        while (results.Count < messages.Count)
        {
            results.Add(new SendEmailResponse
            {
                Success = false,
                Provider = "resend",
                ErrorMessage = "Unknown batch error",
                ErrorType = "provider_error"
            });
        }

        return results.AsReadOnly();
    }

    private static ResendMessage MapToResendMessage(DomainMessage message)
    {
        var resendMessage = new ResendMessage
        {
            From = message.From,
            Subject = message.Subject,
        };

        // Required: To
        resendMessage.To = EmailAddressList.From(message.To);

        // Optional: HtmlBody / TextBody
        if (!string.IsNullOrEmpty(message.HtmlBody))
            resendMessage.HtmlBody = message.HtmlBody;

        if (!string.IsNullOrEmpty(message.TextBody))
            resendMessage.TextBody = message.TextBody;

        // Optional: Cc, Bcc, ReplyTo
        if (message.Cc?.Count > 0)
            resendMessage.Cc = EmailAddressList.From(message.Cc);

        if (message.Bcc?.Count > 0)
            resendMessage.Bcc = EmailAddressList.From(message.Bcc);

        if (message.ReplyTo?.Count > 0)
            resendMessage.ReplyTo = EmailAddressList.From(message.ReplyTo);

        // Optional: Attachments
        if (message.Attachments?.Count > 0)
        {
            resendMessage.Attachments = message.Attachments.Select(a => new Attachment
            {
                Filename = a.Filename,
                Content = Convert.FromBase64String(a.Content),
                ContentType = a.ContentType,
                ContentId = a.ContentId
            }).ToList();
        }

        // Optional: Tags
        if (message.Tags?.Count > 0)
        {
            resendMessage.Tags = message.Tags.Select(t => new ResendTag
            {
                Name = t.Name,
                Value = t.Value
            }).ToList();
        }

        // Optional: Headers
        if (message.Headers?.Count > 0)
        {
            resendMessage.Headers = new Dictionary<string, string>(message.Headers);
        }

        // Optional: ScheduledAt
        if (message.ScheduledAt.HasValue)
        {
            resendMessage.MomentSchedule = message.ScheduledAt.Value.DateTime;
        }

        return resendMessage;
    }

    private static string? ClassifyError(ResendException ex)
    {
        return ex.ErrorType switch
        {
            ErrorType.RateLimitExceeded => "rate_limit",
            ErrorType.DailyQuotaExceeded => "daily_quota",
            ErrorType.MonthlyQuotaExceeded => "monthly_quota",
            _ => "provider_error"
        };
    }
}
