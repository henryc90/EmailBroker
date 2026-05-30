using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using Polly;
using Polly.CircuitBreaker;

namespace EmailBroker.Providers.Resilience;

public sealed class ResilientEmailSender : IEmailSender
{
    private readonly IEmailSender _inner;
    private readonly ResiliencePipeline<SendEmailResponse> _pipeline;

    public ResilientEmailSender(IEmailSender inner, ResiliencePipeline<SendEmailResponse> pipeline)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public async Task<SendEmailResponse> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(
                async ct => await _inner.SendAsync(message, ct),
                cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            return CreateErrorResponse(ex);
        }
        catch (HttpRequestException ex)
        {
            return CreateErrorResponse(ex);
        }
        catch (TimeoutException ex)
        {
            return CreateErrorResponse(ex);
        }
        catch (TaskCanceledException ex)
        {
            return CreateErrorResponse(ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private static SendEmailResponse CreateErrorResponse(Exception ex)
    {
        return new SendEmailResponse
        {
            Success = false,
            ErrorType = "provider_error",
            ErrorMessage = ex.Message
        };
    }

    public async Task<IReadOnlyList<SendEmailResponse>> SendBatchAsync(
        IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default)
    {
        var results = new List<SendEmailResponse>(messages.Count);

        foreach (var message in messages)
        {
            var result = await SendAsync(message, cancellationToken);
            results.Add(result);
        }

        return results.AsReadOnly();
    }
}
