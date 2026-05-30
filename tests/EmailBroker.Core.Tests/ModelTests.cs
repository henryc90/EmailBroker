using EmailBroker.Core.Models;
using FluentAssertions;

namespace EmailBroker.Core.Tests;

public class ModelTests
{
    [Fact]
    public void EmailMessage_can_be_created_with_required_fields()
    {
        var message = new EmailMessage
        {
            From = "sender@example.com",
            To = new[] { "recipient@example.com" },
            Subject = "Test Subject",
            HtmlBody = "<p>Hello</p>"
        };

        message.From.Should().Be("sender@example.com");
        message.To.Should().BeEquivalentTo(new[] { "recipient@example.com" });
        message.Subject.Should().Be("Test Subject");
        message.HtmlBody.Should().Be("<p>Hello</p>");
    }

    [Fact]
    public void EmailMessage_has_correct_defaults()
    {
        var message = new EmailMessage
        {
            From = "a@b.com",
            To = new[] { "c@d.com" },
            Subject = "S"
        };

        message.From.Should().NotBeNull();
        message.To.Should().NotBeNull();
        message.To.Should().HaveCount(1);
        message.Subject.Should().NotBeNull();
        message.TextBody.Should().BeNull();
        message.Cc.Should().BeNull();
        message.Bcc.Should().BeNull();
        message.ReplyTo.Should().BeNull();
        message.Attachments.Should().BeNull();
        message.Tags.Should().BeNull();
        message.Headers.Should().BeNull();
        message.ScheduledAt.Should().BeNull();
    }

    [Fact]
    public void EmailMessage_supports_all_optional_fields()
    {
        var message = new EmailMessage
        {
            From = "sender@example.com",
            To = new[] { "to@example.com" },
            Subject = "Full Test",
            HtmlBody = "<p>Html</p>",
            TextBody = "Text",
            Cc = new[] { "cc@example.com" },
            Bcc = new[] { "bcc@example.com" },
            ReplyTo = new[] { "reply@example.com" },
            Attachments = new[]
            {
                new EmailAttachment
                {
                    Filename = "doc.pdf",
                    Content = "base64content",
                    ContentType = "application/pdf",
                    ContentId = "cid:1"
                }
            },
            Tags = new[]
            {
                new EmailTag { Name = "category", Value = "notification" }
            },
            Headers = new Dictionary<string, string>
            {
                { "X-Custom", "value" }
            },
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        message.From.Should().Be("sender@example.com");
        message.TextBody.Should().Be("Text");
        message.Cc.Should().BeEquivalentTo(new[] { "cc@example.com" });
        message.Bcc.Should().BeEquivalentTo(new[] { "bcc@example.com" });
        message.ReplyTo.Should().BeEquivalentTo(new[] { "reply@example.com" });
        message.Attachments.Should().HaveCount(1);
        message.Attachments![0].Filename.Should().Be("doc.pdf");
        message.Tags.Should().HaveCount(1);
        message.Tags![0].Name.Should().Be("category");
        message.Headers.Should().ContainKey("X-Custom");
        message.ScheduledAt.Should().NotBeNull();
    }

    [Fact]
    public void EmailAttachment_can_be_created_with_minimal_fields()
    {
        var attachment = new EmailAttachment
        {
            Filename = "file.txt",
            Content = "SGVsbG8="
        };

        attachment.Filename.Should().Be("file.txt");
        attachment.Content.Should().Be("SGVsbG8=");
        attachment.ContentType.Should().BeNull();
        attachment.ContentId.Should().BeNull();
    }

    [Fact]
    public void EmailTag_can_be_created()
    {
        var tag = new EmailTag
        {
            Name = "priority",
            Value = "high"
        };

        tag.Name.Should().Be("priority");
        tag.Value.Should().Be("high");
    }

    [Fact]
    public void SendEmailResponse_can_be_created_with_success()
    {
        var response = new SendEmailResponse
        {
            Success = true,
            MessageId = "msg_123",
            Provider = "resend"
        };

        response.Success.Should().BeTrue();
        response.MessageId.Should().Be("msg_123");
        response.Provider.Should().Be("resend");
        response.ErrorMessage.Should().BeNull();
        response.ErrorType.Should().BeNull();
    }

    [Fact]
    public void SendEmailResponse_can_be_created_with_error()
    {
        var response = new SendEmailResponse
        {
            Success = false,
            Provider = "resend",
            ErrorMessage = "Rate limit exceeded",
            ErrorType = "rate_limit"
        };

        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Be("Rate limit exceeded");
        response.ErrorType.Should().Be("rate_limit");
        response.MessageId.Should().BeNull();
    }
}
