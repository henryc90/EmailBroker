using EmailBroker.Core.Abstractions;
using EmailBroker.Providers.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EmailBroker.Providers.Tests;

public class DependencyInjectionTests
{
    private static ServiceProvider BuildServiceProvider(string activeProvider)
    {
        var services = new ServiceCollection();

        var configData = new Dictionary<string, string?>
        {
            ["EmailBroker:ActiveProvider"] = activeProvider,
            ["Resend:ApiToken"] = "re_test",
            ["Resend:ApiUrl"] = "https://api.resend.com",
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "587",
            ["Smtp:UseSsl"] = "false",
            ["SendGrid:ApiKey"] = "SG_test",
            ["Ses:AccessKey"] = "AKIA_TEST",
            ["Ses:SecretKey"] = "test-secret",
            ["Ses:Region"] = "us-east-1",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        services.AddEmailProviders(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddEmailProviders_WhenActiveProviderIsResend_RegistersResilientEmailSenderWrappingResend()
    {
        // Arrange
        using var provider = BuildServiceProvider("resend");

        // Act
        var sender = provider.GetRequiredService<IEmailSender>();

        // Assert
        sender.Should().NotBeNull();
        sender.Should().BeOfType<ResilientEmailSender>();
    }

    [Fact]
    public void AddEmailProviders_WhenActiveProviderIsSmtp_RegistersResilientEmailSenderWrappingSmtp()
    {
        // Arrange
        using var provider = BuildServiceProvider("smtp");

        // Act
        var sender = provider.GetRequiredService<IEmailSender>();

        // Assert
        sender.Should().NotBeNull();
        sender.Should().BeOfType<ResilientEmailSender>();
    }

    [Fact]
    public void AddEmailProviders_WhenActiveProviderIsSendGrid_RegistersResilientEmailSenderWrappingSendGrid()
    {
        // Arrange
        using var provider = BuildServiceProvider("sendgrid");

        // Act
        var sender = provider.GetRequiredService<IEmailSender>();

        // Assert
        sender.Should().NotBeNull();
        sender.Should().BeOfType<ResilientEmailSender>();
    }

    [Fact]
    public void AddEmailProviders_WhenActiveProviderIsSes_RegistersResilientEmailSenderWrappingSes()
    {
        // Arrange
        using var provider = BuildServiceProvider("ses");

        // Act
        var sender = provider.GetRequiredService<IEmailSender>();

        // Assert
        sender.Should().NotBeNull();
        sender.Should().BeOfType<ResilientEmailSender>();
    }
}
