using System.Net.Http.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmailBroker.Providers.Resend;

public class ResendHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;
    private readonly ILogger<ResendHealthCheck> _logger;

    private static HealthCheckResult? _cachedResult;
    private static readonly Lock _cacheLock = new();

    public ResendHealthCheck(HttpClient httpClient, IOptions<ResendOptions> options, ILogger<ResendHealthCheck> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Return cached result after first validation — avoids hitting Resend API on every check
        if (_cachedResult.HasValue)
            return _cachedResult.Value;

        var (apiToken, tokenSource) = ResolveToken();

        if (string.IsNullOrEmpty(apiToken))
        {
            _logger.LogWarning("Resend is not configured — no ApiToken and no Accounts");
            return Cache(HealthCheckResult.Unhealthy("Resend API token is not configured"));
        }

        var truncated = apiToken.Length > 6
            ? apiToken[..(apiToken.IndexOf('_') >= 0 ? apiToken.IndexOf('_') + 4 : 6)] + "***"
            : "***";

        _logger.LogInformation(
            "Resend health check — validating token from {TokenSource}: {TruncatedToken}",
            tokenSource, truncated);

        try
        {
            // POST with empty body — valid token returns 422 (validation error),
            // invalid token returns 401. Never actually sends an email.
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/email")
            {
                Content = JsonContent.Create(new { })
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
            request.Headers.UserAgent.ParseAdd("EmailBroker/1.0");

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableContent)
            {
                _logger.LogInformation("Resend API is reachable — token validated (422). Caching result.");
                return Cache(HealthCheckResult.Healthy("Resend API is reachable"));
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "Resend API returned 401 — token from {TokenSource} ({TruncatedToken}) is invalid",
                    tokenSource, truncated);
                return Cache(HealthCheckResult.Unhealthy("Resend API token is invalid"));
            }

            _logger.LogWarning(
                "Resend API returned unexpected {StatusCode} with token from {TokenSource} ({TruncatedToken})",
                response.StatusCode, tokenSource, truncated);

            return Cache(HealthCheckResult.Unhealthy($"Resend API returned {response.StatusCode}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend API is unreachable");
            return Cache(HealthCheckResult.Unhealthy("Resend API is unreachable", ex));
        }
    }

    private HealthCheckResult Cache(HealthCheckResult result)
    {
        lock (_cacheLock)
        {
            _cachedResult ??= result;
        }
        return _cachedResult!.Value;
    }

    private (string Token, string Source) ResolveToken()
    {
        if (!string.IsNullOrEmpty(_options.ApiToken))
            return (_options.ApiToken, "ApiToken (flat)");

        var accountToken = _options.Accounts?.FirstOrDefault(a => !string.IsNullOrEmpty(a.ApiToken))?.ApiToken;
        if (!string.IsNullOrEmpty(accountToken))
            return (accountToken, "Accounts[0].ApiToken");

        return (string.Empty, "none");
    }
}
