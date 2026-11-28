namespace Errandy.Infrastructure.Payments;

public class PaystackSettings
{
    public const string SectionName = "Paystack";

    // Supply through environment variables or a secret manager, not source control.
    public string SecretKey { get; set; } = string.Empty;

    // Optional trusted frontend URL configured by the server, not by the caller.
    public string? CallbackUrl { get; set; }
}
