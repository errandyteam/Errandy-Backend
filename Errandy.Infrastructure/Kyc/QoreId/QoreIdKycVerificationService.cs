using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Errandy.Application.Common.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Errandy.Infrastructure.Kyc.QoreId;

/// <summary>
/// Real QoreID KYC verifier. Authenticates with client credentials and calls NIN lookup.
/// When credentials aren't configured, falls back to the provided fallback (mock) service.
/// </summary>
public sealed class QoreIdKycVerificationService : IKycVerificationService
{
    private readonly HttpClient _http;
    private readonly QoreIdSettings _settings;
    private readonly ILogger<QoreIdKycVerificationService> _logger;
   
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    public QoreIdKycVerificationService(
    HttpClient http,
    IOptions<QoreIdSettings> settings,
    ILogger<QoreIdKycVerificationService> logger)
    {
        _settings = settings.Value;

        if (string.IsNullOrWhiteSpace(_settings.BaseUrl) ||
            string.IsNullOrWhiteSpace(_settings.ClientId) ||
            string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "Kyc:Provider is 'QoreId' but QoreId:BaseUrl, QoreId:ClientId or QoreId:ClientSecret is not configured.");
        }

        _http = http;
        _logger = logger;
        _http.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<KycVerificationResult> VerifyAsync(RunnerProfile profile, CancellationToken ct)
    {
      
        var docType = (profile.DocumentType ?? string.Empty).Trim().ToUpperInvariant();

        return docType switch
        {
            "NIN" or "NIN_SLIP" => await VerifyNinAsync(profile, ct),
            "BVN" => await VerifyBvnNotImplementedAsync(profile, ct),
            _ => KycVerificationResult.Reject(KycStatus.Rejected,
                $"Document type '{profile.DocumentType}' not supported by QoreID integration. Use NIN (or BVN when enabled).")
        };
    }

    private async Task<KycVerificationResult> VerifyNinAsync(RunnerProfile profile, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        var req = new NinVerifyRequest
        {
            Nin = (profile.DocumentNumber ?? string.Empty).Trim(),
            FirstName = profile.User?.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            LastName = profile.User?.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/verifications/nin")
        {
            Content = JsonContent.Create(req, mediaType: new MediaTypeHeaderValue("application/json"))
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("QoreID NIN verify failed: {Status}, body: {Body}", response.StatusCode, body);
                return KycVerificationResult.Reject(KycStatus.Rejected, "Unable to verify NIN right now. Please try again later.");
            }

            var result = JsonSerializer.Deserialize<NinVerifyResponse>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var verified =
                result?.Status?.Value?.Equals("success", StringComparison.OrdinalIgnoreCase) == true
                && !string.IsNullOrWhiteSpace(result?.Data?.Nin);

            return verified
    ? new KycVerificationResult(true, KycStatus.Pending)
    : KycVerificationResult.Reject(
        KycStatus.Rejected,
        "NIN verification failed: NIN not found or details don't match.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while calling QoreID NIN verification.");
            return KycVerificationResult.Reject(KycStatus.Rejected, "KYC provider error. Please try again later.");
        }
    }

    private Task<KycVerificationResult> VerifyBvnNotImplementedAsync(RunnerProfile profile, CancellationToken ct)
    {
        // BVN is the same pattern; we'll add it once QoreID docs/credentials confirm the exact endpoint.
        return Task.FromResult(KycVerificationResult.Reject(KycStatus.Rejected,
            "BVN verification is not enabled yet. Submit NIN for now."));
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-1))
            return _cachedToken;

        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-1))
                return _cachedToken;

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _settings.ClientSecret,
                ["scope"] = "verification:read"
            });

            using var response = await _http.PostAsync("v1/auth/token", form, ct);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<QoreIdTokenResponse>(cancellationToken: ct)
                                ?? throw new InvalidOperationException("QoreID did not return an access token.");

            _cachedToken = tokenResponse.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresInSeconds);

            return _cachedToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }
}