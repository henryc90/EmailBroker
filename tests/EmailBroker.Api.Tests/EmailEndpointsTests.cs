using System.Linq;
using System.Net;
using System.Net.Http.Json;
using EmailBroker.Api.Models;
using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EmailBroker.Api.Tests;

public class EmailEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly IEmailSender _senderMock;

    public EmailEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _senderMock = Substitute.For<IEmailSender>();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(IEmailSender))
                    .ToList();
                foreach (var d in descriptors)
                    services.Remove(d);

                services.AddSingleton(_senderMock);
            });
        });
    }

    [Fact]
    public async Task PostSendEmail_when_valid_request_returns_200_with_messageId()
    {
        // Arrange
        var request = new SendEmailRequest
        {
            From = "sender@example.com",
            To = ["recipient@example.com"],
            Subject = "Test Subject",
            HtmlBody = "<p>Hello</p>"
        };

        _senderMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(new SendEmailResponse
            {
                Success = true,
                MessageId = "msg_123",
                Provider = "resend"
            });

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/send", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendEmailResponse>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.MessageId.Should().Be("msg_123");
        body.Provider.Should().Be("resend");
    }

    [Fact]
    public async Task PostSendEmail_when_missing_required_fields_returns_400()
    {
        // Arrange
        var request = new SendEmailRequest
        {
            From = string.Empty,
            To = [],
            Subject = string.Empty
        };

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/send", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("From");
        body.Should().Contain("To");
        body.Should().Contain("Subject");
    }

    [Fact]
    public async Task PostSendEmail_when_invalid_email_returns_400()
    {
        // Arrange
        var request = new SendEmailRequest
        {
            From = "not-an-email",
            To = ["recipient@example.com"],
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/send", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("From");
    }

    [Fact]
    public async Task PostSendEmail_when_missing_body_returns_400()
    {
        // Arrange
        var request = new SendEmailRequest
        {
            From = "sender@example.com",
            To = ["recipient@example.com"],
            Subject = "Test"
        };

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/send", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Body");
    }

    [Fact]
    public async Task PostSendEmail_when_subject_too_long_returns_400()
    {
        // Arrange
        var request = new SendEmailRequest
        {
            From = "sender@example.com",
            To = ["recipient@example.com"],
            Subject = new string('x', 999),
            HtmlBody = "<p>Hello</p>"
        };

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/send", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Subject");
    }

    [Fact]
    public async Task PostSendEmail_when_too_many_recipients_returns_400()
    {
        // Arrange
        var request = new SendEmailRequest
        {
            From = "sender@example.com",
            To = Enumerable.Range(0, 51).Select(i => $"recipient{i}@example.com").ToList(),
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/send", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("To");
    }

    [Fact]
    public async Task PostSendEmail_when_attachment_too_large_returns_400()
    {
        // Arrange
        var largeContent = Convert.ToBase64String(new byte[41 * 1024 * 1024]); // ~41 MB

        var request = new SendEmailRequest
        {
            From = "sender@example.com",
            To = ["recipient@example.com"],
            Subject = "Test",
            HtmlBody = "<p>Hello</p>",
            Attachments =
            [
                new EmailAttachmentRequest
                {
                    Filename = "large.pdf",
                    Content = largeContent,
                    ContentType = "application/pdf"
                }
            ]
        };

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/send", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("40 MB");
    }

    [Fact]
    public async Task PostSendBatch_when_valid_request_returns_200()
    {
        // Arrange
        var request = new SendBatchRequest
        {
            Messages =
            [
                new SendEmailRequest
                {
                    From = "sender@example.com",
                    To = ["recipient@example.com"],
                    Subject = "First",
                    HtmlBody = "<p>First</p>"
                },
                new SendEmailRequest
                {
                    From = "sender@example.com",
                    To = ["other@example.com"],
                    Subject = "Second",
                    TextBody = "Plain text"
                }
            ]
        };

        var batchResults = new List<SendEmailResponse>
        {
            new() { Success = true, MessageId = "batch_1", Provider = "resend" },
            new() { Success = true, MessageId = "batch_2", Provider = "resend" }
        }.AsReadOnly();

        _senderMock.SendBatchAsync(Arg.Any<IReadOnlyList<EmailMessage>>(), Arg.Any<CancellationToken>())
            .Returns(batchResults);

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/batch", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<SendEmailResponse>>();
        body.Should().NotBeNull();
        body!.Count.Should().Be(2);
        body[0].MessageId.Should().Be("batch_1");
        body[1].MessageId.Should().Be("batch_2");
    }

    [Fact]
    public async Task PostSendBatch_when_empty_returns_400()
    {
        // Arrange
        var request = new SendBatchRequest
        {
            Messages = []
        };

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/batch", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostSendBatch_when_message_invalid_returns_400()
    {
        // Arrange
        var request = new SendBatchRequest
        {
            Messages =
            [
                new SendEmailRequest
                {
                    From = string.Empty,
                    To = [],
                    Subject = string.Empty
                }
            ]
        };

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/batch", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Messages[0]");
    }

    [Fact]
    public async Task PostSendEmail_when_provider_returns_error_returns_502()
    {
        // Arrange
        var request = new SendEmailRequest
        {
            From = "sender@example.com",
            To = ["recipient@example.com"],
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        _senderMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(new SendEmailResponse
            {
                Success = false,
                Provider = "resend",
                ErrorMessage = "Provider error",
                ErrorType = "provider_error"
            });

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/send", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task PostSendEmail_when_rate_limited_returns_429()
    {
        // Arrange
        var request = new SendEmailRequest
        {
            From = "sender@example.com",
            To = ["recipient@example.com"],
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        _senderMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(new SendEmailResponse
            {
                Success = false,
                Provider = "resend",
                ErrorMessage = "Rate limit exceeded",
                ErrorType = "rate_limit"
            });

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/send", request);

        // Assert
        response.StatusCode.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task PostSendEmail_with_all_fields_returns_200()
    {
        // Arrange
        var request = new SendEmailRequest
        {
            From = "Sender <sender@example.com>",
            To = ["to@example.com"],
            Subject = "Full request",
            HtmlBody = "<p>HTML</p>",
            TextBody = "Plain text",
            Cc = ["cc@example.com"],
            Bcc = ["bcc@example.com"],
            ReplyTo = ["reply@example.com"],
            Attachments =
            [
                new EmailAttachmentRequest
                {
                    Filename = "doc.pdf",
                    Content = Convert.ToBase64String("content"u8.ToArray()),
                    ContentType = "application/pdf"
                }
            ],
            Headers = new Dictionary<string, string> { { "X-Custom", "value" } }
        };

        _senderMock.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(new SendEmailResponse
            {
                Success = true,
                MessageId = "msg_full",
                Provider = "resend"
            });

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/email/send", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
