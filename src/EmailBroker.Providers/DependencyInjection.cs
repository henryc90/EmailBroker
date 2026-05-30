using EmailBroker.Core.Abstractions;
using EmailBroker.Core.Models;
using EmailBroker.Providers.Resend;
using EmailBroker.Providers.Resilience;
using EmailBroker.Providers.SendGrid;
using EmailBroker.Providers.Ses;
using EmailBroker.Providers.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

        // Health checks — only register providers with actual configuration
        var hasResendConfig = !string.IsNullOrEmpty(configuration["Resend:ApiToken"]);
        var hasSmtpConfig = !string.IsNullOrEmpty(configuration["Smtp:Host"]);
        var hasSendGridConfig = !string.IsNullOrEmpty(configuration["SendGrid:ApiKey"]);
        var hasSesConfig = !string.IsNullOrEmpty(configuration["Ses:AccessKey"]);

        if (hasSendGridConfig)
            services.AddHttpClient<SendGridHealthCheck>();
        if (hasResendConfig)
            services.AddHttpClient<ResendHealthCheck>();

        var healthChecks = services.AddHealthChecks();
        if (hasResendConfig)
            healthChecks.AddCheck<ResendHealthCheck>("resend", tags: ["email", "ready"]);
        if (hasSmtpConfig)
            healthChecks.AddCheck<SmtpHealthCheck>("smtp", tags: ["email", "ready"]);
        if (hasSendGridConfig)
            healthChecks.AddCheck<SendGridHealthCheck>("sendgrid", tags: ["email", "ready"]);
        if (hasSesConfig)
            healthChecks.AddCheck<SesHealthCheck>("ses", tags: ["email", "ready"]);

        // Log which providers were configured at startup
        var logger = services.BuildServiceProvider().GetService<ILoggerFactory>()?.CreateLogger("EmailBroker.Providers");
        if (logger is not null)
        {
            logger.LogInformation(
                "Health checks registered — Resend: {Resend}, SMTP: {Smtp}, SendGrid: {SendGrid}, SES: {Ses}",
                hasResendConfig, hasSmtpConfig, hasSendGridConfig, hasSesConfig);
        }

        return services;
    }
}
