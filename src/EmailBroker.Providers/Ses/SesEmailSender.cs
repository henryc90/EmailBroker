using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using Microsoft.Extensions.Options;
using MimeKit;
using DomainMessage = EmailBroker.Core.Models.EmailMessage;
using DomainResponse = EmailBroker.Core.Models.SendEmailResponse;
using SesResponse = Amazon.SimpleEmailV2.Model.SendEmailResponse;

namespace EmailBroker.Providers.Ses;

public class SesEmailSender : IEmailSender
{
    private readonly IAmazonSimpleEmailServiceV2 _client;

    public SesEmailSender(IOptions<SesOptions> options)
    {
        var opt = options.Value;
        var credentials = new BasicAWSCredentials(opt.AccessKey, opt.SecretKey);
        var config = new AmazonSimpleEmailServiceV2Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(opt.Region)
        };
        _client = new AmazonSimpleEmailServiceV2Client(credentials, config);
    }

    internal SesEmailSender(IAmazonSimpleEmailServiceV2 client)
    {
        _client = client;
    }

    public async Task<DomainResponse> SendAsync(DomainMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = BuildRequest(message);

            // Tags
            if (message.Tags is { Count: > 0 })
            {
                request.EmailTags = message.Tags
                    .Select(t => new MessageTag { Name = t.Name, Value = t.Value })
                    .ToList();
            }

            var response = await _client.SendEmailAsync(request, cancellationToken);

            return new DomainResponse
            {
                Success = true,
                MessageId = response.MessageId,
                Provider = "ses"
            };
        }
        catch (AmazonSimpleEmailServiceV2Exception ex)
        {
            return new DomainResponse
            {
                Success = false,
                Provider = "ses",
                ErrorMessage = ex.Message,
                ErrorType = ClassifyError(ex)
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

    private static SendEmailRequest BuildRequest(DomainMessage message)
    {
        var hasAttachments = message.Attachments is { Count: > 0 };

        if (hasAttachments)
        {
            return BuildRawEmailRequest(message);
        }

        return BuildSimpleEmailRequest(message);
    }

    private static SendEmailRequest BuildSimpleEmailRequest(DomainMessage message)
    {
        var request = new SendEmailRequest
        {
            FromEmailAddress = message.From,
            Destination = new Destination
            {
                ToAddresses = message.To.ToList()
            },
            Content = new EmailContent
            {
                Simple = new Amazon.SimpleEmailV2.Model.Message
                {
                    Subject = new Content { Data = message.Subject },
                    Body = new Body()
                }
            }
        };

        if (message.Cc is { Count: > 0 })
            request.Destination.CcAddresses = message.Cc.ToList();

        if (message.Bcc is { Count: > 0 })
            request.Destination.BccAddresses = message.Bcc.ToList();

        if (!string.IsNullOrEmpty(message.HtmlBody))
            request.Content.Simple.Body.Html = new Content { Data = message.HtmlBody };

        if (!string.IsNullOrEmpty(message.TextBody))
            request.Content.Simple.Body.Text = new Content { Data = message.TextBody };

        return request;
    }

    private static SendEmailRequest BuildRawEmailRequest(DomainMessage message)
    {
        var mimeMessage = new MimeMessage();

        // From
        mimeMessage.From.Add(MailboxAddress.Parse(message.From));

        // To
        foreach (var to in message.To)
            mimeMessage.To.Add(MailboxAddress.Parse(to));

        // Cc
        if (message.Cc is { Count: > 0 })
        {
            foreach (var cc in message.Cc)
                mimeMessage.Cc.Add(MailboxAddress.Parse(cc));
        }

        // Bcc
        if (message.Bcc is { Count: > 0 })
        {
            foreach (var bcc in message.Bcc)
                mimeMessage.Bcc.Add(MailboxAddress.Parse(bcc));
        }

        // ReplyTo
        if (message.ReplyTo is { Count: > 0 })
        {
            foreach (var reply in message.ReplyTo)
                mimeMessage.ReplyTo.Add(MailboxAddress.Parse(reply));
        }

        // Subject
        mimeMessage.Subject = message.Subject;

        // Custom headers
        if (message.Headers is { Count: > 0 })
        {
            foreach (var header in message.Headers)
            {
                mimeMessage.Headers.Add(header.Key, header.Value);
            }
        }

        // Body
        var body = new BodyBuilder();

        if (!string.IsNullOrEmpty(message.HtmlBody))
            body.HtmlBody = message.HtmlBody;

        if (!string.IsNullOrEmpty(message.TextBody))
            body.TextBody = message.TextBody;

        // Attachments
        if (message.Attachments is { Count: > 0 })
        {
            foreach (var attachment in message.Attachments)
            {
                var bytes = Convert.FromBase64String(attachment.Content);
                var mimeAttachment = body.Attachments.Add(attachment.Filename, bytes);

                if (!string.IsNullOrEmpty(attachment.ContentType))
                {
                    var parts = attachment.ContentType.Split('/');
                    mimeAttachment.ContentType.MediaType = parts[0];
                    mimeAttachment.ContentType.MediaSubtype = parts.Length > 1 ? parts[1] : "octet-stream";
                }

                if (!string.IsNullOrEmpty(attachment.ContentId))
                {
                    mimeAttachment.ContentId = attachment.ContentId;
                    mimeAttachment.IsAttachment = false;
                }
            }
        }

        mimeMessage.Body = body.ToMessageBody();

        using var stream = new MemoryStream();
        mimeMessage.WriteTo(stream);

        return new SendEmailRequest
        {
            FromEmailAddress = message.From,
            Destination = new Destination
            {
                ToAddresses = message.To.ToList()
            },
            Content = new EmailContent
            {
                Raw = new RawMessage
                {
                    Data = new MemoryStream(stream.ToArray())
                }
            }
        };
    }

    private static string? ClassifyError(AmazonSimpleEmailServiceV2Exception ex)
    {
        return ex.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "rate_limit",
            _ => "provider_error"
        };
    }
}
