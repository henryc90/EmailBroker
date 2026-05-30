using FluentValidation;

namespace EmailBroker.Api.Models;

public class SendEmailRequestValidator : AbstractValidator<SendEmailRequest>
{
    private const int MaxSubjectLength = 998;
    private const int MaxToRecipients = 50;
    private const int MaxAttachmentSizeBytes = 40 * 1024 * 1024; // 40 MB

    public SendEmailRequestValidator()
    {
        RuleFor(x => x.From)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.To)
            .NotEmpty()
            .Must(to => to.Count <= MaxToRecipients)
            .WithMessage($"Maximum {MaxToRecipients} recipients allowed in 'To' field.");

        RuleForEach(x => x.To)
            .EmailAddress();

        RuleFor(x => x.Subject)
            .NotEmpty()
            .MaximumLength(MaxSubjectLength);

        // At least one of HtmlBody or TextBody must be provided
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.HtmlBody) || !string.IsNullOrEmpty(x.TextBody))
            .WithMessage("Either HtmlBody or TextBody must be provided.")
            .OverridePropertyName("Body");

        When(x => x.HtmlBody is not null, () =>
        {
            RuleFor(x => x.HtmlBody!)
                .NotEmpty();
        });

        When(x => x.TextBody is not null, () =>
        {
            RuleFor(x => x.TextBody!)
                .NotEmpty();
        });

        When(x => x.Cc is not null, () =>
        {
            RuleForEach(x => x.Cc!)
                .EmailAddress();
        });

        When(x => x.Bcc is not null, () =>
        {
            RuleForEach(x => x.Bcc!)
                .EmailAddress();
        });

        When(x => x.ReplyTo is not null, () =>
        {
            RuleForEach(x => x.ReplyTo!)
                .EmailAddress();
        });

        When(x => x.Attachments is not null, () =>
        {
            RuleForEach(x => x.Attachments!)
                .ChildRules(a =>
                {
                    a.RuleFor(x => x.Filename)
                        .NotEmpty();
                    a.RuleFor(x => x.Content)
                        .NotEmpty()
                        .Must(BeWithinSizeLimit)
                        .WithMessage("Attachment content exceeds 40 MB limit.");
                });
        });

        When(x => x.ScheduledAt.HasValue, () =>
        {
            RuleFor(x => x.ScheduledAt!.Value)
                .GreaterThan(DateTimeOffset.UtcNow);
        });
    }

    private static bool BeWithinSizeLimit(string base64Content)
    {
        // Estimate decoded byte length from base64 string length
        // Each 4 base64 chars represent up to 3 bytes (ignoring padding)
        var estimatedBytes = (long)base64Content.Length * 3 / 4;
        return estimatedBytes <= MaxAttachmentSizeBytes;
    }
}
