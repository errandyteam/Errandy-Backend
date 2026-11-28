using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Errandy.Application.Common.DTOs;

namespace Errandy.Application.Common.Interfaces;

public interface ICustomService
{
    Task<ApiResponseModel> MakeHttpCall(
        object? request,
        string baseURL,
        string requestURL,
        HttpMethod method,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}
