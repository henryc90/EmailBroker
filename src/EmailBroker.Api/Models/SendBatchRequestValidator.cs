using FluentValidation;

namespace EmailBroker.Api.Models;

public class SendBatchRequestValidator : AbstractValidator<SendBatchRequest>
{
    public SendBatchRequestValidator()
    {
        RuleFor(x => x.Messages)
            .NotEmpty()
            .WithMessage("At least one message is required.");

        RuleForEach(x => x.Messages)
            .SetValidator(new SendEmailRequestValidator());
    }
}
