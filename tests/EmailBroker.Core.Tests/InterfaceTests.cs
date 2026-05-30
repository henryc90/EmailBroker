using System.Linq;
using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using FluentAssertions;

namespace EmailBroker.Core.Tests;

public class InterfaceTests
{
    [Fact]
    public async Task IEmailSender_can_be_implemented_and_returns_SendEmailResponse()
    {
        var stub = new StubEmailSender();
        var message = new EmailMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        var result = await stub.SendAsync(message, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().BeOfType<SendEmailResponse>();
        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("stub_msg_123");
        result.Provider.Should().Be("stub");
    }

    [Fact]
    public async Task IEmailSender_accepts_cancellation_token()
    {
        var stub = new StubEmailSender();
        using var cts = new CancellationTokenSource();

        var message = new EmailMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test",
            HtmlBody = "<p>Hello</p>"
        };

        var result = await stub.SendAsync(message, cts.Token);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    }

    private sealed class StubEmailSender : IEmailSender
    {
        public Task<SendEmailResponse> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SendEmailResponse
            {
                Success = true,
                MessageId = "stub_msg_123",
                Provider = "stub"
            });
        }

        public Task<IReadOnlyList<SendEmailResponse>> SendBatchAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default)
        {
            var results = messages.Select(m => new SendEmailResponse
            {
                Success = true,
                MessageId = $"stub_{Guid.NewGuid()}",
                Provider = "stub"
            }).ToList().AsReadOnly();

            return Task.FromResult<IReadOnlyList<SendEmailResponse>>(results);
        }
    }
}
