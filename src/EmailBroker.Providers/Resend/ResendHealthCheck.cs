using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmailBroker.Providers.Resend;

public class ResendHealthCheck : IHealthCheck
{
    private readonly ResendOptions _options;
    private readonly ILogger<ResendHealthCheck> _logger;

    public ResendHealthCheck(IOptions<ResendOptions> options, ILogger<ResendHealthCheck> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var accounts = _options.Accounts?.Where(a => !string.IsNullOrEmpty(a.ApiToken)).ToList() ?? [];
        var hasFlatToken = !string.IsNullOrEmpty(_options.ApiToken);

        if (!hasFlatToken && accounts.Count == 0)
        {
            _logger.LogWarning("Resend is not configured — no ApiToken and no Accounts");
            return Task.FromResult(HealthCheckResult.Unhealthy("Resend API token is not configured"));
        }

        if (hasFlatToken)
        {
            _logger.LogInformation("Resend configured — ApiToken (flat)");
        }

        foreach (var account in accounts)
        {
            var truncated = account.ApiToken.Length > 6
                ? account.ApiToken[..(account.ApiToken.IndexOf('_') >= 0 ? account.ApiToken.IndexOf('_') + 4 : 6)] + "***"
                : "***";

            _logger.LogInformation(
                "Resend configured — Account domain={Domain}, token={TruncatedToken}",
                account.Domain, truncated);
        }

        return Task.FromResult(HealthCheckResult.Healthy("Resend is configured"));
    }
}
