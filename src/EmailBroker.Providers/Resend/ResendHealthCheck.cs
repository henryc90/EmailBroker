using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EmailBroker.Providers.Resend;

public class ResendHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;

    public ResendHealthCheck(HttpClient httpClient, IOptions<ResendOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
        {
            return HealthCheckResult.Unhealthy("Resend API token is not configured");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.resend.com/emails?limit=1");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiToken);
            request.Headers.UserAgent.ParseAdd("EmailBroker/1.0");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // Any response (200, 404, etc.) means the API is reachable and auth is working
            // A 404 just means no emails found - API is healthy
            // A 401 would mean bad token
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return HealthCheckResult.Healthy("Resend API is reachable");
            }

            return HealthCheckResult.Unhealthy($"Resend API returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Resend API is unreachable", ex);
        }
    }
}
