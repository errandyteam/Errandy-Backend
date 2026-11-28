using System.Text.Json.Serialization;

namespace Errandy.Infrastructure.Kyc.QoreId;

// --- Token ---
internal sealed class QoreIdTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresInSeconds { get; set; }
}

// --- NIN verification ---
internal sealed class NinVerifyRequest
{
    /// <summary>11-digit NIN number.</summary>
    [JsonPropertyName("nin")]
    public string Nin { get; set; } = string.Empty;

    // Optional — some products require these. We include them when we have them.
    [JsonPropertyName("firstname")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastname")]
    public string? LastName { get; set; }
}

internal sealed class NinVerifyResponse
{
    [JsonPropertyName("status")]
    public QoreIdStatus Status { get; set; } = new();
    [JsonPropertyName("data")]
    public NinVerifyData? Data { get; set; }
}

internal sealed class QoreIdStatus
{
    [JsonPropertyName("status")]
    public string? Value { get; set; } // "success" | "failed"
}

internal sealed class NinVerifyData
{
    [JsonPropertyName("nin")]
    public string? Nin { get; set; }
    [JsonPropertyName("firstname")]
    public string? FirstName { get; set; }
    [JsonPropertyName("lastname")]
    public string? LastName { get; set; }
    [JsonPropertyName("middlename")]
    public string? MiddleName { get; set; }
    [JsonPropertyName("dob")]
    public string? Dob { get; set; }
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
    [JsonPropertyName("gender")]
    public string? Gender { get; set; }
}