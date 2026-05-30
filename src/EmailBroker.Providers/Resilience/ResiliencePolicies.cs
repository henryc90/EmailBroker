using EmailBroker.Core.Models;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace EmailBroker.Providers.Resilience;

public static class ResiliencePolicies
{
    public static ResiliencePipeline<SendEmailResponse> CreateDefaultPipeline()
    {
        var retryOptions = new RetryStrategyOptions<SendEmailResponse>
        {
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: HttpRequestException or TimeoutException or TaskCanceledException }
                    => ValueTask.FromResult(true),
                { Result: { Success: false } result }
                    when result.ErrorType is "provider_error" or "rate_limit"
                    => ValueTask.FromResult(true),
                _ => ValueTask.FromResult(false)
            },
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
        };

        var circuitBreakerOptions = new CircuitBreakerStrategyOptions<SendEmailResponse>
        {
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: HttpRequestException or TimeoutException or TaskCanceledException }
                    => ValueTask.FromResult(true),
                { Result: { Success: false } result }
                    when result.ErrorType is "provider_error" or "rate_limit"
                    => ValueTask.FromResult(true),
                _ => ValueTask.FromResult(false)
            },
            FailureRatio = 1.0,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(30),
        };

        return new ResiliencePipelineBuilder<SendEmailResponse>()
            .AddRetry(retryOptions)
            .AddCircuitBreaker(circuitBreakerOptions)
            .Build();
    }
}
