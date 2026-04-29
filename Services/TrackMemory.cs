using KonturVideo.AI.Inference;

namespace KonturVideo.AI.Services;

public sealed class TrackMemory
{
    private readonly Dictionary<string, Dictionary<string, TrackState>> _byCamera = new();

    public IReadOnlyList<TrackState> Match(string cameraName, IReadOnlyList<Detection> detections)
    {
        if (!_byCamera.TryGetValue(cameraName, out var states))
        {
            states = new Dictionary<string, TrackState>();
            _byCamera[cameraName] = states;
        }

        var now = DateTime.UtcNow;
        var result = new List<TrackState>();

        foreach (var detection in detections)
        {
            TrackState? best = null;
            var bestDist = float.MaxValue;

            foreach (var state in states.Values)
            {
                if (!string.Equals(state.Label, detection.Label, StringComparison.OrdinalIgnoreCase))
                    continue;

                var dx = state.CenterX - detection.CenterX;
                var dy = state.CenterY - detection.CenterY;
                var dist = dx * dx + dy * dy;
                if (dist < bestDist && dist < 0.02f)
                {
                    bestDist = dist;
                    best = state;
                }
            }

            if (best is null)
            {
                best = new TrackState
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Label = detection.Label,
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                    CenterX = detection.CenterX,
                    CenterY = detection.CenterY,
                    PrevCenterX = detection.CenterX,
                    PrevCenterY = detection.CenterY
                };
                states[best.Id] = best;
            }
            else
            {
                best.PrevCenterX = best.CenterX;
                best.PrevCenterY = best.CenterY;
                best.CenterX = detection.CenterX;
                best.CenterY = detection.CenterY;
                best.LastSeenUtc = now;
            }

            best.LastDetection = detection;
            result.Add(best);
        }

        var staleIds = states.Values
            .Where(x => (now - x.LastSeenUtc).TotalSeconds > 5)
            .Select(x => x.Id)
            .ToList();

        foreach (var staleId in staleIds)
            states.Remove(staleId);

        return result;
    }
}

public sealed class TrackState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = string.Empty;
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public float PrevCenterX { get; set; }
    public float PrevCenterY { get; set; }
    public Detection? LastDetection { get; set; }
}
