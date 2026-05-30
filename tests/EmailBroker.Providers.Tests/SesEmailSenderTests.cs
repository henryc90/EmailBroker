using System.Net;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using EmailBroker.Core.Abstractions;
using EmailBroker.Providers.Ses;
using FluentAssertions;
using NSubstitute;
using DomainMessage = EmailBroker.Core.Models.EmailMessage;
using DomainAttachment = EmailBroker.Core.Models.EmailAttachment;
using DomainTag = EmailBroker.Core.Models.EmailTag;
using SesResponse = Amazon.SimpleEmailV2.Model.SendEmailResponse;

namespace EmailBroker.Providers.Tests;

public class SesEmailSenderTests
{
    private readonly IAmazonSimpleEmailServiceV2 _clientMock;
    private readonly SesEmailSender _sut;

    public SesEmailSenderTests()
    {
        _clientMock = Substitute.For<IAmazonSimpleEmailServiceV2>();
        _sut = new SesEmailSender(_clientMock);
    }

    [Fact]
    public async Task SendAsync_simple_email_returns_success()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test",
            HtmlBody = "<p>Hello</p>",
            TextBody = "Hello"
        };

        var sesResponse = new SesResponse
        {
            MessageId = "ses-msg-id"
        };

        _clientMock.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(sesResponse);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("ses-msg-id");
        result.Provider.Should().Be("ses");
    }

    [Fact]
    public async Task SendAsync_with_attachments_uses_raw_format()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test with attachment",
            HtmlBody = "<p>See attached</p>",
            Attachments = new[]
            {
                new DomainAttachment
                {
                    Filename = "doc.pdf",
                    Content = Convert.ToBase64String("PDF content"u8.ToArray()),
                    ContentType = "application/pdf"
                }
            }
        };

        var sesResponse = new SesResponse
        {
            MessageId = "ses-raw-id"
        };

        SendEmailRequest? capturedRequest = null;
        _clientMock.SendEmailAsync(
                Arg.Do<SendEmailRequest>(x => capturedRequest = x),
                Arg.Any<CancellationToken>())
            .Returns(sesResponse);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("ses-raw-id");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Content.Raw.Should().NotBeNull();
        capturedRequest.Content.Simple.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_simple_email_uses_simple_format()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        var sesResponse = new SesResponse { MessageId = "ses-simple-id" };

        SendEmailRequest? capturedRequest = null;
        _clientMock.SendEmailAsync(
                Arg.Do<SendEmailRequest>(x => capturedRequest = x),
                Arg.Any<CancellationToken>())
            .Returns(sesResponse);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Success.Should().BeTrue();
        capturedRequest!.Content.Simple.Should().NotBeNull();
        capturedRequest.Content.Raw.Should().BeNull();
        capturedRequest.Content.Simple.Subject.Data.Should().Be("Test");
        capturedRequest.Content.Simple.Body.Html.Data.Should().Be("<p>Hello</p>");
        capturedRequest.Destination.ToAddresses.Should().Contain("recipient@example.com");
    }

    [Fact]
    public async Task SendAsync_when_invalid_credentials_returns_provider_error()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        var exception = new AmazonSimpleEmailServiceV2Exception(
            "Invalid credentials",
            ErrorType.Sender,
            "InvalidClientTokenId",
            "request-id",
            HttpStatusCode.Unauthorized);

        _clientMock.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SesResponse>(exception));

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be("provider_error");
        result.Provider.Should().Be("ses");
    }

    [Fact]
    public async Task SendAsync_when_rate_limited_returns_rate_limit_error()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        var exception = new AmazonSimpleEmailServiceV2Exception(
            "Rate limit exceeded",
            ErrorType.Receiver,
            "TooManyRequestsException",
            "request-id",
            HttpStatusCode.TooManyRequests);

        _clientMock.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SesResponse>(exception));

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be("rate_limit");
    }

    [Fact]
    public async Task SendBatchAsync_processes_all_messages()
    {
        // Arrange
        var messages = new[]
        {
            new DomainMessage
            {
                From = "a@b.com",
                To = new[] { "to1@b.com" },
                Subject = "Msg 1",
                HtmlBody = "<p>1</p>"
            },
            new DomainMessage
            {
                From = "a@b.com",
                To = new[] { "to2@b.com" },
                Subject = "Msg 2",
                HtmlBody = "<p>2</p>"
            }
        };

        var sesResponse = new SesResponse { MessageId = "ses-id" };

        _clientMock.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(sesResponse);

        // Act
        var results = await _sut.SendBatchAsync(messages);

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
        await _clientMock.Received(2).SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>());
    }
}
