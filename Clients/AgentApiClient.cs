using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using KonturVideo.AI.Models;

namespace KonturVideo.AI.Clients;

public sealed class AgentApiClient
{
    private readonly HttpClient _http;
    private readonly AgentApiOptions _options;

    public AgentApiClient(HttpClient http, IOptions<AgentApiOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
    }

    public async Task<IReadOnlyList<AgentCameraDto>> GetCamerasAsync(CancellationToken cancellationToken)
    {
        var path = _options.CamerasPath.TrimStart('/');
        var result = await _http.GetFromJsonAsync<List<AgentCameraDto>>(path, cancellationToken);
        return result ?? new List<AgentCameraDto>();
    }
}
