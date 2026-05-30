namespace EmailBroker.Providers.Resend;

public class ResendOptions
{
    public string ApiUrl { get; set; } = "https://api.resend.com";

    /// <summary>Default API token (fallback when no account matches the domain).</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>Multi-account configuration: one entry per domain.</summary>
    public List<ResendAccount> Accounts { get; set; } = [];
}
