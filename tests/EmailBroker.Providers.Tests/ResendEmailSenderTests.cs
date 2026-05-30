using EmailBroker.Core.Abstractions;
using EmailBroker.Providers.Resend;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using DomainMessage = EmailBroker.Core.Models.EmailMessage;
using DomainAttachment = EmailBroker.Core.Models.EmailAttachment;
using DomainTag = EmailBroker.Core.Models.EmailTag;

namespace EmailBroker.Providers.Tests;

public class ResendEmailSenderTests
{
    private readonly global::Resend.IResend _resendMock;
    private readonly ResendEmailSender _sut;

    public ResendEmailSenderTests()
    {
        _resendMock = Substitute.For<global::Resend.IResend>();
        var options = Options.Create(new ResendOptions
        {
            ApiUrl = "https://api.resend.com",
            ApiToken = "re_123"
        });
        _sut = new ResendEmailSender(options, _resendMock);
    }

    [Fact]
    public async Task SendAsync_should_call_EmailSendAsync_and_return_success()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test Subject",
            HtmlBody = "<p>Hello</p>"
        };

        var expectedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sdkResponse = new global::Resend.ResendResponse<Guid>(
            expectedId,
            new global::Resend.ResendRateLimit());

        _resendMock.EmailSendAsync(
                Arg.Any<global::Resend.EmailMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(sdkResponse);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        result.Provider.Should().Be("resend");

        await _resendMock.Received(1).EmailSendAsync(
            Arg.Any<global::Resend.EmailMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_with_minimal_fields_should_not_throw()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "a@b.com",
            To = new[] { "c@d.com" },
            Subject = "Minimal",
            TextBody = "plain text"
        };

        var sdkResponse = new global::Resend.ResendResponse<Guid>(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            new global::Resend.ResendRateLimit());

        _resendMock.EmailSendAsync(
                Arg.Any<global::Resend.EmailMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(sdkResponse);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    }

    [Fact]
    public async Task SendAsync_with_null_optionals_should_not_throw()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "a@b.com",
            To = new[] { "c@d.com" },
            Subject = "Null Optionals",
            HtmlBody = "<p>test</p>",
            Cc = null,
            Bcc = null,
            ReplyTo = null,
            Attachments = null,
            Tags = null,
            Headers = null,
            ScheduledAt = null
        };

        var sdkResponse = new global::Resend.ResendResponse<Guid>(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            new global::Resend.ResendRateLimit());

        _resendMock.EmailSendAsync(
                Arg.Any<global::Resend.EmailMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(sdkResponse);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_should_map_all_fields_correctly()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "Sender <sender@example.com>",
            To = new[] { "to@example.com" },
            Subject = "Full Mapping",
            HtmlBody = "<html><body><p>HTML</p></body></html>",
            TextBody = "Plain text version",
            Cc = new[] { "cc@example.com" },
            Bcc = new[] { "bcc@example.com" },
            ReplyTo = new[] { "reply@example.com" },
            Attachments = new[]
            {
                new DomainAttachment
                {
                    Filename = "report.pdf",
                    Content = Convert.ToBase64String("PDF content"u8.ToArray()),
                    ContentType = "application/pdf",
                    ContentId = "cid:report"
                }
            },
            Tags = new[]
            {
                new DomainTag { Name = "category", Value = "notification" }
            },
            Headers = new Dictionary<string, string>
            {
                { "X-Custom", "custom-value" }
            },
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(2)
        };

        var sdkResponse = new global::Resend.ResendResponse<Guid>(
            Guid.NewGuid(),
            new global::Resend.ResendRateLimit());

        global::Resend.EmailMessage? capturedMessage = null;

        _resendMock.EmailSendAsync(
            Arg.Do<global::Resend.EmailMessage>(x => capturedMessage = x),
            Arg.Any<CancellationToken>())
            .Returns(sdkResponse);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Success.Should().BeTrue();

        capturedMessage.Should().NotBeNull();
        capturedMessage!.From.Should().NotBeNull();
        capturedMessage.To.Should().NotBeNull();
        capturedMessage.Subject.Should().Be("Full Mapping");
        capturedMessage.HtmlBody.Should().Be("<html><body><p>HTML</p></body></html>");
        capturedMessage.TextBody.Should().Be("Plain text version");
        capturedMessage.Cc.Should().NotBeNull();
        capturedMessage.Bcc.Should().NotBeNull();
        capturedMessage.ReplyTo.Should().NotBeNull();
        capturedMessage.Attachments.Should().HaveCount(1);
        capturedMessage.Tags.Should().HaveCount(1);
        capturedMessage.Headers.Should().ContainKey("X-Custom");
        capturedMessage.MomentSchedule.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_when_ResendException_should_return_error_response()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "a@b.com",
            To = new[] { "c@d.com" },
            Subject = "Error Test",
            HtmlBody = "<p>boom</p>"
        };

        var exception = new global::Resend.ResendException(
            System.Net.HttpStatusCode.InternalServerError,
            global::Resend.ErrorType.ApplicationError,
            "Internal server error",
            new global::Resend.ResendRateLimit());

        _resendMock.EmailSendAsync(
                Arg.Any<global::Resend.EmailMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<global::Resend.ResendResponse<Guid>>(exception));

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Provider.Should().Be("resend");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.ErrorType.Should().Be("provider_error");
        result.MessageId.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_when_rate_limit_exceeded_should_classify_correctly()
    {
        await AssertErrorClassification(global::Resend.ErrorType.RateLimitExceeded, "rate_limit");
    }

    [Fact]
    public async Task SendAsync_when_daily_quota_exceeded_should_classify_correctly()
    {
        await AssertErrorClassification(global::Resend.ErrorType.DailyQuotaExceeded, "daily_quota");
    }

    [Fact]
    public async Task SendAsync_when_monthly_quota_exceeded_should_classify_correctly()
    {
        await AssertErrorClassification(global::Resend.ErrorType.MonthlyQuotaExceeded, "monthly_quota");
    }

    private async Task AssertErrorClassification(global::Resend.ErrorType errorType, string expectedClassification)
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "a@b.com",
            To = new[] { "c@d.com" },
            Subject = "Quota Test",
            HtmlBody = "<p>test</p>"
        };

        var exception = new global::Resend.ResendException(
            System.Net.HttpStatusCode.TooManyRequests,
            errorType,
            "Quota exceeded",
            new global::Resend.ResendRateLimit());

        _resendMock.EmailSendAsync(
                Arg.Any<global::Resend.EmailMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<global::Resend.ResendResponse<Guid>>(exception));

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(expectedClassification);
    }
}
