using MailKit.Net.Smtp;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EmailBroker.Providers.Smtp;

public class SmtpHealthCheck : IHealthCheck
{
    private readonly SmtpOptions _options;

    public SmtpHealthCheck(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.Host))
        {
            return HealthCheckResult.Unhealthy("SMTP host is not configured");
        }

        try
        {
            using var client = new SmtpClient();

            await client.ConnectAsync(_options.Host, _options.Port, _options.UseSsl, cancellationToken);

            if (!string.IsNullOrEmpty(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password!, cancellationToken);
            }

            await client.DisconnectAsync(true, cancellationToken);

            return HealthCheckResult.Healthy("SMTP server is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SMTP server is unreachable", ex);
        }
    }
}
