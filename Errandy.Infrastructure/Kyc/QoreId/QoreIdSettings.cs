namespace Errandy.Infrastructure.Kyc.QoreId;

public class QoreIdSettings
{
    public const string SectionName = "QoreId";

    /// <summary>QoreID base URL, e.g. https://api.qoreid.com/</summary>
    public string BaseUrl { get; set; } = "https://api.qoreid.com/";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// If true and credentials are missing, use the mock KYC verifier.
    /// </summary>
    public bool UseMockIfNotConfigured { get; set; } = true;
}