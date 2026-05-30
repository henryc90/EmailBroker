using System.Net;
using EmailBroker.Core.Abstractions;
using EmailBroker.Providers.SendGrid;
using FluentAssertions;
using NSubstitute;
using SendGrid;
using SendGrid.Helpers.Mail;
using DomainMessage = EmailBroker.Core.Models.EmailMessage;
using DomainAttachment = EmailBroker.Core.Models.EmailAttachment;
using DomainTag = EmailBroker.Core.Models.EmailTag;

namespace EmailBroker.Providers.Tests;

public class SendGridEmailSenderTests
{
    private readonly ISendGridClient _clientMock;
    private readonly SendGridEmailSender _sut;

    public SendGridEmailSenderTests()
    {
        _clientMock = Substitute.For<ISendGridClient>();
        _sut = new SendGridEmailSender(_clientMock);
    }

    private static Response CreateResponse(HttpStatusCode statusCode, string? messageId = null)
    {
        var httpResponse = new HttpResponseMessage(statusCode);

        if (messageId is not null)
        {
            httpResponse.Headers.Add("X-Message-Id", messageId);
        }

        var body = statusCode switch
        {
            HttpStatusCode.OK => new StringContent("{}"),
            HttpStatusCode.Unauthorized => new StringContent("{\"errors\":[{\"message\":\"Unauthorized\"}]}"),
            HttpStatusCode.Forbidden => new StringContent("{\"errors\":[{\"message\":\"Forbidden\"}]}"),
            HttpStatusCode.TooManyRequests => new StringContent("{\"errors\":[{\"message\":\"Rate limit\"}]}"),
            _ => new StringContent("{}")
        };

        return new Response(statusCode, body, httpResponse.Headers);
    }

    [Fact]
    public async Task SendAsync_on_success_returns_success_with_message_id()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        var response = CreateResponse(HttpStatusCode.OK, "test-message-id");

        _clientMock.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("test-message-id");
        result.Provider.Should().Be("sendgrid");
    }

    [Fact]
    public async Task SendAsync_on_unauthorized_returns_provider_error()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        var response = CreateResponse(HttpStatusCode.Unauthorized);

        _clientMock.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be("provider_error");
        result.Provider.Should().Be("sendgrid");
    }

    [Fact]
    public async Task SendAsync_on_rate_limit_returns_rate_limit_error()
    {
        // Arrange
        var message = new DomainMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        var response = CreateResponse(HttpStatusCode.TooManyRequests);

        _clientMock.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be("rate_limit");
    }

    [Fact]
    public async Task SendBatchAsync_processes_each_message_and_returns_per_item_results()
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
            },
            new DomainMessage
            {
                From = "a@b.com",
                To = new[] { "to3@b.com" },
                Subject = "Msg 3",
                HtmlBody = "<p>3</p>"
            }
        };

        var successResponse = CreateResponse(HttpStatusCode.OK, "msg-id");

        _clientMock.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(successResponse);

        // Act
        var results = await _sut.SendBatchAsync(messages);

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
        await _clientMock.Received(3).SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_maps_all_fields_correctly()
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

        var response = CreateResponse(HttpStatusCode.OK, "msg-id");

        SendGridMessage? capturedMessage = null;
        _clientMock.SendEmailAsync(
                Arg.Do<SendGridMessage>(x => capturedMessage = x),
                Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Success.Should().BeTrue();

        capturedMessage.Should().NotBeNull();
        capturedMessage!.From.Email.Should().Be("sender@example.com");
        capturedMessage.From.Name.Should().Be("Sender");
        capturedMessage.Subject.Should().Be("Full Mapping");
        capturedMessage.HtmlContent.Should().Be("<html><body><p>HTML</p></body></html>");
        capturedMessage.PlainTextContent.Should().Be("Plain text version");
        capturedMessage.Personalizations.Should().HaveCount(1);
        capturedMessage.Personalizations[0].Tos.Should().HaveCount(1);
        capturedMessage.Personalizations[0].Tos[0].Email.Should().Be("to@example.com");
        capturedMessage.Personalizations[0].Ccs.Should().HaveCount(1);
        capturedMessage.Personalizations[0].Ccs[0].Email.Should().Be("cc@example.com");
        capturedMessage.Personalizations[0].Bccs.Should().HaveCount(1);
        capturedMessage.Personalizations[0].Bccs[0].Email.Should().Be("bcc@example.com");
        capturedMessage.ReplyTo.Email.Should().Be("reply@example.com");
        capturedMessage.Attachments.Should().HaveCount(1);
        capturedMessage.Attachments[0].Filename.Should().Be("report.pdf");
        capturedMessage.Headers.Should().ContainKey("X-Custom");
        capturedMessage.Categories.Should().Contain("category:notification");
        capturedMessage.SendAt.Should().NotBeNull();
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

        var response = CreateResponse(HttpStatusCode.OK, "optional-msg-id");

        _clientMock.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act
        var result = await _sut.SendAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("optional-msg-id");
    }
}
