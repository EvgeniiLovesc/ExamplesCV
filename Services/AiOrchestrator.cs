using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using KonturVideo.AI.Clients;
using KonturVideo.AI.Inference;
using KonturVideo.AI.Models;
using System.Collections.Concurrent;

namespace KonturVideo.AI.Services;

public sealed class AiOrchestrator : BackgroundService
{
    private readonly AgentApiClient _agentApi;
    private readonly WebUiClient _webUi;
    private readonly FrameSampler _frameSampler;
    private readonly IObjectDetector _detector;
    private readonly RuleMatcher _matcher;
    private readonly TrackMemory _tracks;
    private readonly AiOptions _options;
    private readonly ILogger<AiOrchestrator> _logger;

    private readonly ConcurrentDictionary<string, CameraWorkerHandle> _workers = new(StringComparer.OrdinalIgnoreCase);
    private volatile Dictionary<string, IReadOnlyList<AiRuleDto>> _rulesByCamera = new(StringComparer.OrdinalIgnoreCase);

    public AiOrchestrator(
        AgentApiClient agentApi,
        WebUiClient webUi,
        FrameSampler frameSampler,
        IObjectDetector detector,
        RuleMatcher matcher,
        TrackMemory tracks,
        IOptions<AiOptions> options,
        ILogger<AiOrchestrator> logger)
    {
        _agentApi = agentApi;
        _webUi = webUi;
        _frameSampler = frameSampler;
        _detector = detector;
        _matcher = matcher;
        _tracks = tracks;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cameras = await _agentApi.GetCamerasAsync(stoppingToken);
                var rules = await _webUi.GetRulesAsync(stoppingToken);

                //_rulesByCamera = rules
                //    .Where(x => x.Enabled)
                //    .GroupBy(x => x.CameraName, StringComparer.OrdinalIgnoreCase)
                //    .ToDictionary(
                //        x => x.Key,
                //        x => (IReadOnlyList<AiRuleDto>)x.ToList(),
                //        StringComparer.OrdinalIgnoreCase);

                _rulesByCamera = rules
                    .Where(x => x.Enabled)
                    .GroupBy(x => x.CameraId ?? x.CameraName)
                    .ToDictionary(
                        x => x.Key,
                        x => (IReadOnlyList<AiRuleDto>)x.ToList(),
                        StringComparer.OrdinalIgnoreCase);

                var activeCameras = cameras
                    .Where(c => c.Enabled && c.Status == 2 && !string.IsNullOrWhiteSpace(c.RtspUrl))
                    .ToList();

                var activeIds = activeCameras
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                    .Select(c => c.Id!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var camera in activeCameras)
                {
                    if (string.IsNullOrWhiteSpace(camera.Id))
                        continue;

                    if (_workers.ContainsKey(camera.Id))
                        continue;

                    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var task = RunCameraWorkerAsync(camera, linkedCts.Token);

                    var handle = new CameraWorkerHandle(camera, linkedCts, task);
                    if (_workers.TryAdd(camera.Id, handle))
                    {
                        _logger.LogInformation("Started AI worker for {Camera}", camera.Name);

                        _ = task.ContinueWith(t =>
                        {
                            _workers.TryRemove(camera.Id, out _);

                            if (t.IsFaulted)
                                _logger.LogWarning(t.Exception, "AI worker crashed for {Camera}", camera.Name);
                            else
                                _logger.LogInformation("AI worker stopped for {Camera}", camera.Name);

                            linkedCts.Dispose();
                        }, TaskScheduler.Default);
                    }
                    else
                    {
                        linkedCts.Cancel();
                        linkedCts.Dispose();
                    }
                }

                foreach (var worker in _workers.Where(x => !activeIds.Contains(x.Key)).Select(x => x.Value).ToList())
                {
                    _logger.LogInformation("Stopping AI worker for {Camera}", worker.Camera.Name);
                    worker.Cts.Cancel();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI orchestration tick failed.");
            }

            await Task.Delay(_options.IdleDelayMs, stoppingToken);
        }

        foreach (var worker in _workers.Values)
            worker.Cts.Cancel();

        try
        {
            await Task.WhenAll(_workers.Values.Select(x => x.Task));
        }
        catch
        {
        }
    }

    private async Task RunCameraWorkerAsync(AgentCameraDto camera, CancellationToken cancellationToken)
    {
        var overlayKey = !string.IsNullOrWhiteSpace(camera.StreamKey) ? camera.StreamKey : camera.Name;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // var sourceUrl = camera.RtspUrl!.Replace("/stream1", "/stream2");
                // var sourceUrl = camera.RtspUrl!;
                var sourceUrl = $"rtsp://localhost:8554/{camera.StreamKey}";

                _logger.LogInformation("Opening source for {Camera}: {Source}", camera.Name, sourceUrl);

                if (!_frameSampler.TryOpen(sourceUrl, out _))
                {
                    _logger.LogWarning("Failed to open stream for {Camera}: {Source}", camera.Name, sourceUrl);
                    await Task.Delay(3000, cancellationToken);
                    continue;
                }

                _logger.LogInformation("FFmpeg stream opened for {Camera}", camera.Name);

                var noFrameSinceUtc = DateTime.UtcNow;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var tickStartedAtUtc = DateTime.UtcNow;
                    var readStartedAtUtc = DateTime.UtcNow;

                    if (!_frameSampler.TryReadFrame(out var frame))
                    {
                        if (!_frameSampler.IsRunning)
                        {
                            _logger.LogWarning("Frame sampler stopped for {Camera}, reconnecting...", camera.Name);
                            break;
                        }

                        if ((DateTime.UtcNow - noFrameSinceUtc).TotalSeconds > 3)
                        {
                            _logger.LogWarning("No frames for {Camera} during 3 seconds, reconnecting...", camera.Name);
                            break;
                        }

                        await Task.Delay(20, cancellationToken);
                        continue;
                    }

                    noFrameSinceUtc = DateTime.UtcNow;
                    var readFinishedAtUtc = DateTime.UtcNow;

                    //_logger.LogInformation(
                    //    "Frame read time for {Camera}: {ReadMs} ms",
                    //    camera.Name,
                    //    (readFinishedAtUtc - readStartedAtUtc).TotalMilliseconds);

                    //_logger.LogInformation(
                    //    "Frame received for {Camera}: {Width}x{Height}",
                    //    camera.Name,
                    //    frame.Width,
                    //    frame.Height);

                    var capturedAtUtc = _frameSampler.LastFrameUtc;

                    using (frame)
                    {
                        var detectStartedAtUtc = DateTime.UtcNow;

                        var detections = _detector.Detect(frame, cancellationToken);

                        //_logger.LogInformation(
                        //    "Raw detections for {Camera}: {Labels}",
                        //    camera.Name,
                        //    detections.Count == 0
                        //        ? "(none)"
                        //        : string.Join(", ", detections.Select(d => $"{d.Label}:{d.Confidence:F2}")));

                        var personDetections = detections
                            .Where(d => !string.IsNullOrWhiteSpace(d.Label))
                            .Where(d => d.Label.Trim().Equals("person", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        //_logger.LogInformation(
                        //    "Filtered person detections for {Camera}: {Count}",
                        //    camera.Name,
                        //    personDetections.Count);

                        var detectFinishedAtUtc = DateTime.UtcNow;

                        var tracks = _tracks.Match(camera.Name, personDetections);

                        DateTime? overlayPublishedAtUtc = null;

                        if (_options.OverlayEveryFrame)
                        {
                            await _webUi.IngestOverlayAsync(
                                overlayKey,
                                personDetections,
                                capturedAtUtc,
                                detectFinishedAtUtc,
                                cancellationToken);

                            overlayPublishedAtUtc = DateTime.UtcNow;
                        }

                        //_logger.LogInformation(
                        //    "AI timing {Camera}: capture->detectStart={CaptureToDetectStartMs} ms, detect={DetectMs} ms, detect->overlayPublish={DetectToPublishMs} ms",
                        //    camera.Name,
                        //    (detectStartedAtUtc - capturedAtUtc).TotalMilliseconds,
                        //    (detectFinishedAtUtc - detectStartedAtUtc).TotalMilliseconds,
                        //    overlayPublishedAtUtc.HasValue
                        //        ? (overlayPublishedAtUtc.Value - detectFinishedAtUtc).TotalMilliseconds
                        //        : -1);

                        //var rules = _rulesByCamera.TryGetValue(camera.Name, out var rulesForCamera)
                        //    ? rulesForCamera
                        //    : Array.Empty<AiRuleDto>();

                        var key = camera.Id ?? camera.Name;

                        var rules = _rulesByCamera.TryGetValue(key, out var rulesForCamera)
                            ? rulesForCamera
                            : Array.Empty<AiRuleDto>();

                        var events = _matcher.Evaluate(camera.Name, camera.Id, rules, personDetections, tracks);

                        foreach (var ev in events)
                        {
                            ev.CameraId = camera.Id ?? string.Empty;
                            ev.CameraName = camera.Name;

                            try
                            {
                                var fileName = $"{overlayKey}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.jpg";
                                var snapshotsDir = Path.Combine(AppContext.BaseDirectory, "snapshots");
                                Directory.CreateDirectory(snapshotsDir);

                                var filePath = Path.Combine(snapshotsDir, fileName);
                                Cv2.ImWrite(filePath, frame);

                                await _webUi.IngestEventWithSnapshotAsync(ev, filePath, cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Snapshot failed for {Camera}", camera.Name);
                                await _webUi.IngestEventAsync(ev, cancellationToken);
                            }
                        }

                        //_logger.LogInformation(
                        //    "AI tick {Camera}: detections={DetectionCount}, events={EventCount}",
                        //    camera.Name,
                        //    personDetections.Count,
                        //    events.Count);
                    }

                    // ограничение FPS (вместо fps= в ffmpeg)
                    var elapsedMs = (DateTime.UtcNow - tickStartedAtUtc).TotalMilliseconds;
                    var targetMs = 1000.0 / Math.Max(1, _options.FramesPerSecond);
                    var delayMs = targetMs - elapsedMs;

                    if (delayMs > 0)
                    {
                        await Task.Delay((int)delayMs, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI worker loop failed for {Camera}", camera.Name);
                await Task.Delay(1000, cancellationToken);
            }
            finally
            {
                _frameSampler.Close();
            }
        }
    }

    private sealed record CameraWorkerHandle(
        AgentCameraDto Camera,
        CancellationTokenSource Cts,
        Task Task);
}