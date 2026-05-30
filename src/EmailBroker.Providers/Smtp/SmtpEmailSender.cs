using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EmailBroker.Providers.Smtp;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task<SendEmailResponse> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var mimeMessage = BuildMimeMessage(message);

            using var client = new SmtpClient();

            await client.ConnectAsync(_options.Host, _options.Port, _options.UseSsl, cancellationToken);

            if (!string.IsNullOrEmpty(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password!, cancellationToken);
            }

            var response = await client.SendAsync(mimeMessage, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return new SendEmailResponse
            {
                Success = true,
                MessageId = response,
                Provider = "smtp"
            };
        }
        catch (Exception ex)
        {
            return new SendEmailResponse
            {
                Success = false,
                Provider = "smtp",
                ErrorMessage = ex.Message,
                ErrorType = "provider_error"
            };
        }
    }

    public async Task<IReadOnlyList<SendEmailResponse>> SendBatchAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default)
    {
        var results = new List<SendEmailResponse>(messages.Count);

        // SMTP doesn't support true batch — send sequentially
        foreach (var message in messages)
        {
            var result = await SendAsync(message, cancellationToken);
            results.Add(result);
        }

        return results.AsReadOnly();
    }

    private static MimeMessage BuildMimeMessage(EmailMessage message)
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
                    mimeAttachment.ContentType.MediaType = attachment.ContentType.Split('/')[0];
                    mimeAttachment.ContentType.MediaSubtype = attachment.ContentType.Split('/')[1];
                }

                if (!string.IsNullOrEmpty(attachment.ContentId))
                {
                    mimeAttachment.ContentId = attachment.ContentId;
                    mimeAttachment.IsAttachment = false;
                }
            }
        }

        mimeMessage.Body = body.ToMessageBody();

        return mimeMessage;
    }
}
