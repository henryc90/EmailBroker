using System.Net.Http.Headers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EmailBroker.Providers.SendGrid;

public class SendGridHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly SendGridOptions _options;

    public SendGridHealthCheck(HttpClient httpClient, IOptions<SendGridOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            return HealthCheckResult.Unhealthy("SendGrid API key is not configured");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.sendgrid.com/v3/scopes");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            request.Headers.UserAgent.ParseAdd("EmailBroker/1.0");

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("SendGrid API is reachable");
            }

            return HealthCheckResult.Unhealthy($"SendGrid API returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SendGrid API is unreachable", ex);
        }
    }
}
