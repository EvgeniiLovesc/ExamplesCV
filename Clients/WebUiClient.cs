using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using KonturVideo.AI.Inference;
using KonturVideo.AI.Models;
using KonturVideo.AI.Services;

namespace KonturVideo.AI.Clients;

public sealed class WebUiClient
{
    private readonly HttpClient _http;
    private readonly WebUiOptions _options;
    private string? _token;
    private DateTime _tokenUtc;

    public WebUiClient(HttpClient http, IOptions<WebUiOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_token) && (DateTime.UtcNow - _tokenUtc) < TimeSpan.FromHours(12))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return;
        }

        var response = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest
        {
            Username = _options.Username,
            Password = _options.Password
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("WebUI login returned empty response.");

        _token = payload.Token;
        _tokenUtc = DateTime.UtcNow;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    public async Task<IReadOnlyList<AiRuleDto>> GetRulesAsync(CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var rules = await _http.GetFromJsonAsync<List<AiRuleDto>>("api/rules", cancellationToken);
        return rules ?? new List<AiRuleDto>();
    }

    public async Task IngestOverlayAsync(
        string cameraName,
        IReadOnlyList<Detection> detections,
        DateTime frameCapturedAtUtc,
        DateTime inferenceCompletedAtUtc,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new OverlayIngestRequest
        {
            CameraName = cameraName,
            TimestampUtc = frameCapturedAtUtc,
            FrameCapturedAtUtc = frameCapturedAtUtc,
            InferenceCompletedAtUtc = inferenceCompletedAtUtc,
            PublishedAtUtc = DateTime.UtcNow,
            Boxes = detections.Select(d => new OverlayBoxDto
            {
                Id = d.Id,
                Label = d.Label,
                Confidence = d.Confidence,
                X = d.X,
                Y = d.Y,
                W = d.W,
                H = d.H
            }).ToList()
        };

        using var response = await _http.PostAsJsonAsync("api/overlay/ingest", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
    public async Task IngestEventAsync(MatchedEvent matchedEvent, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new AiEventIngestRequest
        {
            CameraName = matchedEvent.CameraName,
            CameraId = matchedEvent.CameraId,
            RuleId = matchedEvent.RuleId,
            RuleName = matchedEvent.RuleName,
            EventType = matchedEvent.EventType,
            TargetClass = matchedEvent.TargetClass,
            Severity = matchedEvent.Severity,
            TimestampUtc = matchedEvent.TimestampUtc,
            Message = $"{matchedEvent.RuleName}: {matchedEvent.TargetClass}",
            Boxes = matchedEvent.Boxes.Select(d => new OverlayBoxDto
            {
                Id = d.Id,
                Label = d.Label,
                Confidence = d.Confidence,
                X = d.X,
                Y = d.Y,
                W = d.W,
                H = d.H
            }).ToList()
        };

        using var response = await _http.PostAsJsonAsync("api/events/ingest", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task IngestEventWithSnapshotAsync(MatchedEvent ev, string filePath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var base64 = Convert.ToBase64String(bytes);

        var payload = new
        {
            cameraName = ev.CameraName,
            cameraId = ev.CameraId,
            ruleId = ev.RuleId,
            ruleName = ev.RuleName,
            eventType = ev.EventType,
            targetClass = ev.TargetClass,
            severity = ev.Severity,
            timestampUtc = ev.TimestampUtc,
            message = $"{ev.TargetClass} detected",
            snapshotBase64 = base64,
            boxes = ev.Boxes.Select(x => new
            {
                id = x.Id,
                label = x.Label,
                confidence = x.Confidence,
                x = x.X,
                y = x.Y,
                w = x.W,
                h = x.H
            })
        };

        await _http.PostAsJsonAsync("api/events/ingest", payload, cancellationToken);
    }
}
