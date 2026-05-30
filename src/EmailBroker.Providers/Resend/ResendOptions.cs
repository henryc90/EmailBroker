namespace EmailBroker.Providers.Resend;

public class ResendOptions
{
    public string ApiUrl { get; set; } = "https://api.resend.com";

    /// <summary>Resend API token.</summary>
    public string ApiToken { get; set; } = string.Empty;
}
