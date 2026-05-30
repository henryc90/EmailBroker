using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using EmailBroker.Providers.Resilience;
using FluentAssertions;
using NSubstitute;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace EmailBroker.Providers.Tests;

public class ResilientEmailSenderTests
{
    private static ValueTask<bool> ShouldHandle(Outcome<SendEmailResponse> outcome)
    {
        return outcome switch
        {
            { Exception: HttpRequestException or TimeoutException or TaskCanceledException }
                => ValueTask.FromResult(true),
            { Result: { Success: false } result }
                when result.ErrorType is "provider_error" or "rate_limit"
                => ValueTask.FromResult(true),
            _ => ValueTask.FromResult(false)
        };
    }

    /// <summary>
    /// Creates a pipeline with zero-delay retries for fast unit testing.
    /// </summary>
    private static ResiliencePipeline<SendEmailResponse> CreateTestPipeline(int maxRetries = 3)
    {
        var retryOptions = new RetryStrategyOptions<SendEmailResponse>
        {
            ShouldHandle = args => ShouldHandle(args.Outcome),
            MaxRetryAttempts = maxRetries,
            DelayGenerator = _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.Zero),
            BackoffType = DelayBackoffType.Constant,
        };

        return new ResiliencePipelineBuilder<SendEmailResponse>()
            .AddRetry(retryOptions)
            .Build();
    }

    /// <summary>
    /// Creates a pipeline with circuit breaker only (no retry) for testing.
    /// MinimumThroughput must be ≥ 2, BreakDuration must be ≥ 500ms in Polly 8.x.
    /// </summary>
    private static ResiliencePipeline<SendEmailResponse> CreateCircuitBreakerPipeline(int threshold)
    {
        var cbOptions = new CircuitBreakerStrategyOptions<SendEmailResponse>
        {
            ShouldHandle = args => ShouldHandle(args.Outcome),
            FailureRatio = 1.0,
            MinimumThroughput = Math.Max(2, threshold),
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(1),
        };

        return new ResiliencePipelineBuilder<SendEmailResponse>()
            .AddCircuitBreaker(cbOptions)
            .Build();
    }

    private readonly IEmailSender _innerMock;

    public ResilientEmailSenderTests()
    {
        _innerMock = Substitute.For<IEmailSender>();
    }

    private static EmailMessage CreateTestMessage() => new()
    {
        From = "sender@example.com",
        To = new[] { "recipient@example.com" },
        Subject = "Test Subject",
        HtmlBody = "<p>Hello</p>"
    };

    // ──────────────────────────────────────────────
    //  a. SendAsync returns inner result on success
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_OnSuccess_ReturnsInnerResult()
    {
        // Arrange
        var pipeline = CreateTestPipeline();
        var sut = new ResilientEmailSender(_innerMock, pipeline);
        var message = CreateTestMessage();
        var expected = new SendEmailResponse { Success = true, MessageId = "msg-1", Provider = "resend" };

        _innerMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await sut.SendAsync(message);

        // Assert
        result.Should().BeSameAs(expected);
        await _innerMock.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────
    //  b. SendAsync retries on HttpRequestException, exhausts,
    //     returns provider_error
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_OnHttpRequestException_RetriesAndReturnsProviderError()
    {
        // Arrange
        var pipeline = CreateTestPipeline(maxRetries: 2); // initial + 2 retries = 3 total
        var sut = new ResilientEmailSender(_innerMock, pipeline);
        var message = CreateTestMessage();

        _innerMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SendEmailResponse>(new HttpRequestException("Network error")));

        // Act
        var result = await sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be("provider_error");

        await _innerMock.Received(3).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────
    //  c. SendBatchAsync retries on HttpRequestException
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendBatchAsync_OnHttpRequestException_RetriesAndReturnsProviderError()
    {
        // Arrange
        var pipeline = CreateTestPipeline(maxRetries: 2);
        var sut = new ResilientEmailSender(_innerMock, pipeline);
        var messages = new[] { CreateTestMessage() };

        _innerMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SendEmailResponse>(new HttpRequestException("Network error")));

        // Act
        var results = await sut.SendBatchAsync(messages);

        // Assert
        results.Should().HaveCount(1);
        results[0].Success.Should().BeFalse();
        results[0].ErrorType.Should().Be("provider_error");

        await _innerMock.Received(3).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────
    //  d. SendAsync retries on rate_limit result, succeeds on retry
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_OnRateLimitResult_RetriesAndSucceeds()
    {
        // Arrange
        var pipeline = CreateTestPipeline(maxRetries: 2);
        var sut = new ResilientEmailSender(_innerMock, pipeline);
        var message = CreateTestMessage();

        var rateLimitResponse = new SendEmailResponse
        {
            Success = false,
            Provider = "resend",
            ErrorType = "rate_limit",
            ErrorMessage = "Rate limit exceeded"
        };

        var successResponse = new SendEmailResponse
        {
            Success = true,
            MessageId = "msg-after-retry",
            Provider = "resend"
        };

        _innerMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(rateLimitResponse, successResponse);

        // Act
        var result = await sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("msg-after-retry");

        await _innerMock.Received(2).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────
    //  e. Circuit breaker opens after threshold failures
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WhenCircuitBreakerOpens_ReturnsProviderError()
    {
        // Arrange
        // MinimumThroughput must be ≥ 2 in Polly 8.x, so circuit opens after 2 failures.
        var pipeline = CreateCircuitBreakerPipeline(threshold: 2);
        var sut = new ResilientEmailSender(_innerMock, pipeline);
        var message = CreateTestMessage();

        _innerMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SendEmailResponse>(new HttpRequestException("fail")));

        // First call — will fail, circuit counts 1 failure
        await sut.SendAsync(message);

        // Second call — will fail, circuit reaches threshold and opens
        await sut.SendAsync(message);

        // Act: third call — circuit should be open
        var result = await sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be("provider_error");

        // Only 2 calls should reach the inner sender (3rd is rejected by circuit breaker)
        await _innerMock.Received(2).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────
    //  f. Non-transient exception passes through without retry
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_OnNonTransientException_DoesNotRetryAndPropagates()
    {
        // Arrange
        var pipeline = CreateTestPipeline();
        var sut = new ResilientEmailSender(_innerMock, pipeline);
        var message = CreateTestMessage();

        _innerMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SendEmailResponse>(new InvalidOperationException("Non-transient error")));

        // Act
        Func<Task> act = () => sut.SendAsync(message);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        await _innerMock.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────────────────────
    //  g. Cancellation propagates correctly
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WhenCancelled_PropagatesCancellation()
    {
        // Arrange
        var pipeline = CreateTestPipeline();
        var sut = new ResilientEmailSender(_innerMock, pipeline);
        var message = CreateTestMessage();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _innerMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SendEmailResponse>(new OperationCanceledException()));

        // Act
        Func<Task> act = () => sut.SendAsync(message, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
