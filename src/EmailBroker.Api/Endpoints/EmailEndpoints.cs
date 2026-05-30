using EmailBroker.Api.Models;
using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using FluentValidation;

namespace EmailBroker.Api.Endpoints;

public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this WebApplication app)
    {
        app.MapPost("/api/email/send", async (
            SendEmailRequest request,
            IEmailSender emailSender,
            IValidator<SendEmailRequest> validator,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var message = MapToDomain(request);
            var result = await emailSender.SendAsync(message, ct);

            return result.Success
                ? Results.Ok(result)
                : result.ErrorType switch
                {
                    "rate_limit" or "daily_quota" or "monthly_quota"
                        => Results.StatusCode(429),
                    _ => Results.StatusCode(502)
                };
        })
        .WithName("SendEmail");

        app.MapPost("/api/email/batch", async (
            SendBatchRequest request,
            IEmailSender emailSender,
            IValidator<SendBatchRequest> validator,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            var messages = request.Messages.Select(MapToDomain).ToList().AsReadOnly();
            var results = await emailSender.SendBatchAsync(messages, ct);

            return Results.Ok(results);
        })
        .WithName("SendEmailBatch");
    }

    private static EmailMessage MapToDomain(SendEmailRequest request)
    {
        return new EmailMessage
        {
            From = request.From,
            To = request.To.AsReadOnly(),
            Subject = request.Subject,
            HtmlBody = request.HtmlBody,
            TextBody = request.TextBody,
            Cc = request.Cc?.AsReadOnly(),
            Bcc = request.Bcc?.AsReadOnly(),
            ReplyTo = request.ReplyTo?.AsReadOnly(),
            Headers = request.Headers,
            ScheduledAt = request.ScheduledAt,
            Attachments = request.Attachments?
                .Select(a => new EmailAttachment
                {
                    Filename = a.Filename,
                    Content = a.Content,
                    ContentType = a.ContentType,
                    ContentId = a.ContentId,
                })
                .ToList()
                .AsReadOnly()
        };
    }
}
