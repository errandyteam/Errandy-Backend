using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;
using Errandy.Application.Common.Interfaces;

namespace Errandy.Infrastructure.Services;

public class CustomService : ICustomService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public CustomService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<ApiResponseModel> MakeHttpCall(
        object? request,
        string baseURL,
        string requestURL,
        HttpMethod method,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        if (method != HttpMethod.Post && method != HttpMethod.Get)
            throw new NotSupportedException($"HTTP method {method} is not supported.");

        // CreateClient returns a lightweight client; the factory manages handler pooling.
        using var client = _httpClientFactory.CreateClient("ExternalApi");
        client.BaseAddress = new Uri(baseURL, UriKind.Absolute);

        using var httpRequest = new HttpRequestMessage(method, requestURL);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (headers is not null)
        {
            foreach (var header in headers)
                httpRequest.Headers.Add(header.Key, header.Value);
        }

        if (method == HttpMethod.Post)
        {
            var json = JsonSerializer.Serialize(request);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ApiResponseModel
        {
            Body = body,
            StatusCode = (int)response.StatusCode
        };
    }
}
