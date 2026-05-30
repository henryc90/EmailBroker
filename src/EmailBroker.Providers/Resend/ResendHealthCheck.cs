using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmailBroker.Providers.Resend;

public class ResendHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;
    private readonly ILogger<ResendHealthCheck> _logger;

    public ResendHealthCheck(HttpClient httpClient, IOptions<ResendOptions> options, ILogger<ResendHealthCheck> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
            return HealthCheckResult.Unhealthy("Resend API token is not configured");

        var (apiToken, tokenSource) = ResolveToken();

        var truncated = apiToken.Length > 6
            ? apiToken[..(apiToken.IndexOf('_') >= 0 ? apiToken.IndexOf('_') + 4 : 6)] + "***"
            : "***";

        _logger.LogInformation(
            "Resend health check — using token from {TokenSource}: {TruncatedToken}",
            tokenSource, truncated);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.resend.com/emails?limit=1");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
            request.Headers.UserAgent.ParseAdd("EmailBroker/1.0");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // Any response (200, 404, etc.) means the API is reachable and auth is working
            // A 404 just means no emails found - API is healthy
            // A 401 would mean bad token
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Resend API is reachable — {StatusCode}", response.StatusCode);
                return HealthCheckResult.Healthy("Resend API is reachable");
            }

            _logger.LogWarning(
                "Resend API returned {StatusCode} with token from {TokenSource} ({TruncatedToken})",
                response.StatusCode, tokenSource, truncated);

            return HealthCheckResult.Unhealthy($"Resend API returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend API is unreachable");
            return HealthCheckResult.Unhealthy("Resend API is unreachable", ex);
        }
    }

    private bool IsConfigured() => !string.IsNullOrEmpty(_options.ApiToken);

    private (string Token, string Source) ResolveToken()
        => (_options.ApiToken, "ApiToken");
}
