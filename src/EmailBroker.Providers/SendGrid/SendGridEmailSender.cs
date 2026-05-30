using System.Net;
using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using DomainMessage = EmailBroker.Core.Models.EmailMessage;
using DomainResponse = EmailBroker.Core.Models.SendEmailResponse;
using SendGridAttachment = SendGrid.Helpers.Mail.Attachment;

namespace EmailBroker.Providers.SendGrid;

public class SendGridEmailSender : IEmailSender
{
    private readonly ISendGridClient _client;

    public SendGridEmailSender(IOptions<SendGridOptions> options)
    {
        _client = new SendGridClient(options.Value.ApiKey);
    }

    internal SendGridEmailSender(ISendGridClient client)
    {
        _client = client;
    }

    public async Task<DomainResponse> SendAsync(DomainMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var msg = MapToSendGridMessage(message);
            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                var messageId = ParseMessageId(response);
                return new DomainResponse
                {
                    Success = true,
                    MessageId = messageId,
                    Provider = "sendgrid"
                };
            }

            var errorBody = await response.Body.ReadAsStringAsync(cancellationToken);
            return new DomainResponse
            {
                Success = false,
                Provider = "sendgrid",
                ErrorMessage = errorBody,
                ErrorType = ClassifyError(response.StatusCode)
            };
        }
        catch (Exception ex)
        {
            return new DomainResponse
            {
                Success = false,
                Provider = "sendgrid",
                ErrorMessage = ex.Message,
                ErrorType = "provider_error"
            };
        }
    }

    public async Task<IReadOnlyList<DomainResponse>> SendBatchAsync(
        IReadOnlyList<DomainMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DomainResponse>(messages.Count);

        foreach (var message in messages)
        {
            var result = await SendAsync(message, cancellationToken);
            results.Add(result);
        }

        return results.AsReadOnly();
    }

    private static SendGridMessage MapToSendGridMessage(DomainMessage message)
    {
        var msg = new SendGridMessage();

        // From
        msg.From = ParseEmailAddress(message.From);

        // Personalization (To, Cc, Bcc)
        var personalization = new Personalization
        {
            Tos = message.To.Select(ParseEmailAddress).ToList()
        };

        if (message.Cc is { Count: > 0 })
            personalization.Ccs = message.Cc.Select(ParseEmailAddress).ToList();

        if (message.Bcc is { Count: > 0 })
            personalization.Bccs = message.Bcc.Select(ParseEmailAddress).ToList();

        msg.Personalizations = [personalization];

        // Subject
        msg.Subject = message.Subject;

        // Body
        if (!string.IsNullOrEmpty(message.HtmlBody))
            msg.HtmlContent = message.HtmlBody;

        if (!string.IsNullOrEmpty(message.TextBody))
            msg.PlainTextContent = message.TextBody;

        // ReplyTo (use first if multiple)
        if (message.ReplyTo is { Count: > 0 })
            msg.ReplyTo = ParseEmailAddress(message.ReplyTo[0]);

        // Attachments
        if (message.Attachments is { Count: > 0 })
        {
            msg.Attachments = message.Attachments.Select(a => new SendGridAttachment
            {
                Filename = a.Filename,
                Content = a.Content,
                Type = a.ContentType ?? "application/octet-stream",
                Disposition = a.ContentId != null ? "inline" : "attachment",
                ContentId = a.ContentId
            }).ToList();
        }

        // Headers
        if (message.Headers is { Count: > 0 })
            msg.Headers = new Dictionary<string, string>(message.Headers);

        // Categories (from Tags)
        if (message.Tags is { Count: > 0 })
        {
            msg.Categories = message.Tags.Select(t => $"{t.Name}:{t.Value}").ToList();
        }

        // ScheduledAt -> Unix timestamp
        if (message.ScheduledAt.HasValue)
        {
            msg.SendAt = (int)message.ScheduledAt.Value.ToUnixTimeSeconds();
        }

        return msg;
    }

    private static EmailAddress ParseEmailAddress(string address)
    {
        var idx = address.IndexOf('<');
        if (idx > 0 && address.EndsWith('>'))
        {
            var name = address[..idx].Trim();
            var email = address[(idx + 1)..^1].Trim();
            return new EmailAddress(email, name);
        }
        return new EmailAddress(address.Trim());
    }

    private static string? ParseMessageId(Response response)
    {
        if (response.Headers is not null &&
            response.Headers.TryGetValues("X-Message-Id", out var values))
        {
            return values.FirstOrDefault();
        }
        return null;
    }

    private static string? ClassifyError(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.TooManyRequests => "rate_limit",
            HttpStatusCode.Forbidden => "daily_quota",
            HttpStatusCode.Unauthorized => "provider_error",
            _ => "provider_error"
        };
    }
}
