using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using EmailBroker.Providers.Resend;
using EmailBroker.Providers.Resilience;
using EmailBroker.Providers.SendGrid;
using EmailBroker.Providers.Ses;
using EmailBroker.Providers.Smtp;
using Microsoft.Extensions.Configuration;
using Polly;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace EmailBroker.Providers;

public static class DependencyInjection
{
    public static IServiceCollection AddEmailProviders(this IServiceCollection services, IConfiguration configuration)
    {
        // Resend provider
        services.Configure<ResendOptions>(configuration.GetSection("Resend"));
        services.AddKeyedSingleton<IEmailSender, ResendRouterSender>("resend");

        // SMTP provider
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        services.AddKeyedSingleton<IEmailSender, SmtpEmailSender>("smtp");

        // SendGrid provider
        services.Configure<SendGridOptions>(configuration.GetSection("SendGrid"));
        services.AddKeyedSingleton<IEmailSender, SendGridEmailSender>("sendgrid");

        // SES provider
        services.Configure<SesOptions>(configuration.GetSection("Ses"));
        services.AddKeyedSingleton<IEmailSender, SesEmailSender>("ses");

        // Pipeline is a singleton (thread-safe, shared instance)
        var pipeline = ResiliencePolicies.CreateDefaultPipeline();
        services.AddSingleton(pipeline);

        // Default: resolve active provider from config, wrapped with resilience
        var activeProvider = configuration.GetValue<string>("EmailBroker:ActiveProvider") ?? "resend";
        services.AddSingleton<IEmailSender>(sp =>
        {
            var inner = sp.GetRequiredKeyedService<IEmailSender>(activeProvider);
            var resiliencePipeline = sp.GetRequiredService<ResiliencePipeline<SendEmailResponse>>();
            return new ResilientEmailSender(inner, resiliencePipeline);
        });

        // Health checks
        services.AddHttpClient<SendGridHealthCheck>();
        services.AddHttpClient<ResendHealthCheck>();
        services.AddHealthChecks()
            .AddCheck<ResendHealthCheck>("resend", tags: ["email", "ready"])
            .AddCheck<SmtpHealthCheck>("smtp", tags: ["email", "ready"])
            .AddCheck<SendGridHealthCheck>("sendgrid", tags: ["email", "ready"])
            .AddCheck<SesHealthCheck>("ses", tags: ["email", "ready"]);

        return services;
    }
}
