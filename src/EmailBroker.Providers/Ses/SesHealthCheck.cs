using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EmailBroker.Providers.Ses;

public class SesHealthCheck : IHealthCheck
{
    private readonly SesOptions _options;

    public SesHealthCheck(IOptions<SesOptions> options)
    {
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.AccessKey) || string.IsNullOrEmpty(_options.SecretKey))
            return HealthCheckResult.Unhealthy("SES credentials are not configured");

        try
        {
            var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
            var config = new AmazonSimpleEmailServiceV2Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region)
            };

            using var client = new AmazonSimpleEmailServiceV2Client(credentials, config);
            await client.GetAccountAsync(new Amazon.SimpleEmailV2.Model.GetAccountRequest(), cancellationToken);

            return HealthCheckResult.Healthy("SES API is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SES API is unreachable", ex);
        }
    }
}
