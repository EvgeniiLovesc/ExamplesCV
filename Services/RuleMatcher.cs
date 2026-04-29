using System.Reflection;
using KonturVideo.AI.Inference;
using KonturVideo.AI.Models;

namespace KonturVideo.AI.Services;

public sealed class RuleMatcher
{
    private readonly Dictionary<string, DateTime> _cooldowns = new();

    public List<MatchedEvent> Evaluate(
        string cameraName,
        string? cameraId,
        IReadOnlyList<AiRuleDto> rules,
        IReadOnlyList<Detection> detections,
        IReadOnlyList<TrackState> tracks)
    {
        var now = DateTime.UtcNow;
        var events = new List<MatchedEvent>();

        foreach (var rule in rules.Where(x => x.Enabled && MatchesCamera(x, cameraId, cameraName)))
        {
            var targetClasses = GetTargetClasses(rule);

            var candidates = detections
                .Where(d => d.Confidence >= rule.Threshold)
                .Where(d => MatchesTargetClass(d, targetClasses))
                .ToList();

            if (candidates.Count == 0)
                continue;

            var matchedBoxes = candidates;
            var isMatch = false;

            if (IsLineCrossingRule(rule))
            {
                if (TryGetRuleLine(rule, out var ax, out var ay, out var bx, out var by, out var direction))
                {
                    var trackCrossed = tracks.Any(t => TrackCrossesLine(t, targetClasses, ax, ay, bx, by, direction));
                    var boxCrossed = candidates.Any(d => DetectionIntersectsLine(d, ax, ay, bx, by));
                    isMatch = trackCrossed || boxCrossed;

                    if (isMatch && !trackCrossed)
                    {
                        matchedBoxes = candidates
                            .Where(d => DetectionIntersectsLine(d, ax, ay, bx, by))
                            .ToList();
                    }
                }
            }
            else
            {
                matchedBoxes = candidates
                    .Where(d => DetectionMatchesPolygon(rule, d))
                    .ToList();

                isMatch = matchedBoxes.Count > 0;
            }

            if (!isMatch || matchedBoxes.Count == 0)
                continue;

            var cooldownKey = $"{cameraName}:{rule.Id}";
            if (_cooldowns.TryGetValue(cooldownKey, out var until) && until > now)
                continue;

            _cooldowns[cooldownKey] = now.AddSeconds(Math.Max(0, rule.CooldownSeconds));

            events.Add(new MatchedEvent
            {
                CameraName = cameraName,
                CameraId = cameraId ?? string.Empty,
                RuleId = rule.Id,
                RuleName = rule.Name,
                EventType = rule.EventType,
                TargetClass = rule.TargetClass,
                Severity = rule.Severity,
                TimestampUtc = now,
                Boxes = matchedBoxes
            });
        }

        return events;
    }

    private static bool MatchesCamera(AiRuleDto rule, string? cameraId, string cameraName)
    {
        if (!string.IsNullOrWhiteSpace(cameraId) && !string.IsNullOrWhiteSpace(rule.CameraId))
            return string.Equals(rule.CameraId, cameraId, StringComparison.OrdinalIgnoreCase);

        return string.Equals(rule.CameraName, cameraName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLineCrossingRule(AiRuleDto rule)
    {
        var value = (rule.EventType ?? string.Empty).Trim().ToLowerInvariant();
        return value is "line-crossing" or "tripwire";
    }

    private static HashSet<string> GetTargetClasses(AiRuleDto rule)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCsv(result, rule.TargetClass);

        var filters = rule.GetType().GetProperty("Filters")?.GetValue(rule);
        var classesCsv = GetString(filters, "ClassesCsv", "classesCsv", "TargetClasses", "targetClasses");
        AddCsv(result, classesCsv);

        return result;
    }

    private static void AddCsv(HashSet<string> set, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, "any", StringComparison.OrdinalIgnoreCase))
                continue;

            set.Add(part);
        }
    }

    private static bool MatchesTargetClass(Detection detection, HashSet<string> targetClasses)
    {
        if (targetClasses.Count == 0)
            return true;

        var label = GetDetectionLabel(detection);
        return !string.IsNullOrWhiteSpace(label) && targetClasses.Contains(label);
    }

    private static string? GetDetectionLabel(Detection detection)
    {
        return GetString(detection, "Label", "Class", "ClassName", "Category", "TargetClass");
    }

    private static bool TryGetRuleLine(AiRuleDto rule, out double ax, out double ay, out double bx, out double by, out string direction)
    {
        ax = ay = bx = by = 0;
        direction = "both";

        var region = rule.GetType().GetProperty("Region")?.GetValue(rule);
        if (region is null)
            return false;

        direction = (GetString(region, "Direction", "direction") ?? "both").Trim().ToLowerInvariant();

        var lineObj = region.GetType().GetProperty("Line")?.GetValue(region)
                      ?? region.GetType().GetProperty("line")?.GetValue(region);

        if (lineObj is not System.Collections.IEnumerable lineEnum)
            return false;

        var points = new List<object>();
        foreach (var item in lineEnum)
        {
            if (item is not null)
                points.Add(item);
        }

        if (points.Count < 2)
            return false;

        ax = GetDouble(points[0], "X", "x");
        ay = GetDouble(points[0], "Y", "y");
        bx = GetDouble(points[1], "X", "x");
        by = GetDouble(points[1], "Y", "y");

        return true;
    }

    private static bool TrackCrossesLine(TrackState track, HashSet<string> targetClasses, double ax, double ay, double bx, double by, string direction)
    {
        var label = GetString(track, "Label", "Class", "ClassName", "Category", "TargetClass");
        if (targetClasses.Count > 0 && (string.IsNullOrWhiteSpace(label) || !targetClasses.Contains(label)))
            return false;

        if (!TryGetTrackCenters(track, out var prevX, out var prevY, out var currX, out var currY))
            return false;

        var prevSide = SideOfLine(ax, ay, bx, by, prevX, prevY);
        var currSide = SideOfLine(ax, ay, bx, by, currX, currY);

        if (Math.Abs(prevSide) < 1e-9 || Math.Abs(currSide) < 1e-9)
            return false;

        if (Math.Sign(prevSide) == Math.Sign(currSide))
            return false;

        return direction switch
        {
            "a-to-b" => prevSide > 0 && currSide < 0,
            "b-to-a" => prevSide < 0 && currSide > 0,
            _ => true
        };
    }

    private static bool TryGetTrackCenters(TrackState track, out double prevX, out double prevY, out double currX, out double currY)
    {
        prevX = prevY = currX = currY = 0;

        if (TryGetCenterFromObject(track, new[] { "PreviousCenter", "PrevCenter", "LastCenter", "FromCenter" }, out prevX, out prevY) &&
            TryGetCenterFromObject(track, new[] { "CurrentCenter", "Center", "CurrCenter", "ToCenter" }, out currX, out currY))
        {
            return true;
        }

        var hasPrev = TryGetPair(track, out prevX, out prevY,
            ("PreviousCenterX", "PreviousCenterY"),
            ("PrevCenterX", "PrevCenterY"),
            ("PreviousX", "PreviousY"),
            ("PrevX", "PrevY"));

        var hasCurr = TryGetPair(track, out currX, out currY,
            ("CurrentCenterX", "CurrentCenterY"),
            ("CurrCenterX", "CurrCenterY"),
            ("CenterX", "CenterY"),
            ("X", "Y"));

        return hasPrev && hasCurr;
    }

    private static bool TryGetCenterFromObject(object source, string[] objectNames, out double x, out double y)
    {
        x = y = 0;
        foreach (var name in objectNames)
        {
            var nested = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(source);
            if (nested is null)
                continue;

            x = GetDouble(nested, "X", "x");
            y = GetDouble(nested, "Y", "y");
            return true;
        }

        return false;
    }

    private static bool TryGetPair(object source, out double x, out double y, params (string x, string y)[] variants)
    {
        x = y = 0;
        foreach (var variant in variants)
        {
            var px = source.GetType().GetProperty(variant.x, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var py = source.GetType().GetProperty(variant.y, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (px is null || py is null)
                continue;

            x = Convert.ToDouble(px.GetValue(source) ?? 0d);
            y = Convert.ToDouble(py.GetValue(source) ?? 0d);
            return true;
        }

        return false;
    }

    private static bool DetectionIntersectsLine(Detection detection, double ax, double ay, double bx, double by)
    {
        var x = GetDouble(detection, "X", "Left", "left");
        var y = GetDouble(detection, "Y", "Top", "top");
        var w = GetDouble(detection, "W", "Width", "width");
        var h = GetDouble(detection, "H", "Height", "height");

        var left = x;
        var top = y;
        var right = x + w;
        var bottom = y + h;

        if (PointInRect(ax, ay, left, top, right, bottom) || PointInRect(bx, by, left, top, right, bottom))
            return true;

        return SegmentsIntersect(ax, ay, bx, by, left, top, right, top)
            || SegmentsIntersect(ax, ay, bx, by, right, top, right, bottom)
            || SegmentsIntersect(ax, ay, bx, by, right, bottom, left, bottom)
            || SegmentsIntersect(ax, ay, bx, by, left, bottom, left, top);
    }

    private static bool DetectionMatchesPolygon(AiRuleDto rule, Detection detection)
    {
        var polygon = GetRulePolygon(rule);
        if (polygon.Count < 3)
            return true;

        var x = GetDouble(detection, "X", "Left", "left");
        var y = GetDouble(detection, "Y", "Top", "top");
        var w = GetDouble(detection, "W", "Width", "width");
        var h = GetDouble(detection, "H", "Height", "height");

        var cx = x + (w / 2.0);
        var cy = y + (h / 2.0);

        return IsPointInPolygon(cx, cy, polygon);
    }

    private static List<(double x, double y)> GetRulePolygon(AiRuleDto rule)
    {
        var result = new List<(double x, double y)>();

        var region = rule.GetType().GetProperty("Region")?.GetValue(rule);
        if (region is null)
            return result;

        var polygonObj = region.GetType().GetProperty("Polygon")?.GetValue(region)
                        ?? region.GetType().GetProperty("polygon")?.GetValue(region);

        if (polygonObj is not System.Collections.IEnumerable polygonEnum)
            return result;

        foreach (var item in polygonEnum)
        {
            if (item is null)
                continue;

            var x = GetDouble(item, "X", "x");
            var y = GetDouble(item, "Y", "y");
            result.Add((x, y));
        }

        return result;
    }

    private static bool IsPointInPolygon(double px, double py, IReadOnlyList<(double x, double y)> polygon)
    {
        var inside = false;
        var j = polygon.Count - 1;

        for (var i = 0; i < polygon.Count; i++)
        {
            var xi = polygon[i].x;
            var yi = polygon[i].y;
            var xj = polygon[j].x;
            var yj = polygon[j].y;

            var intersects =
                ((yi > py) != (yj > py)) &&
                (px < (xj - xi) * (py - yi) / ((yj - yi) == 0 ? 1e-12 : (yj - yi)) + xi);

            if (intersects)
                inside = !inside;

            j = i;
        }

        return inside;
    }


    private static bool PointInRect(double px, double py, double left, double top, double right, double bottom)
        => px >= left && px <= right && py >= top && py <= bottom;

    private static bool SegmentsIntersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        var d1 = SideOfLine(ax, ay, bx, by, cx, cy);
        var d2 = SideOfLine(ax, ay, bx, by, dx, dy);
        var d3 = SideOfLine(cx, cy, dx, dy, ax, ay);
        var d4 = SideOfLine(cx, cy, dx, dy, bx, by);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

        return false;
    }

    private static double SideOfLine(double ax, double ay, double bx, double by, double px, double py)
        => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

    private static string? GetString(object? source, params string[] names)
    {
        if (source is null)
            return null;

        foreach (var name in names)
        {
            var prop = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null)
                continue;

            var value = prop.GetValue(source)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static double GetDouble(object source, params string[] names)
    {
        foreach (var name in names)
        {
            var prop = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null)
                continue;

            var value = prop.GetValue(source);
            if (value is null)
                continue;

            try
            {
                return Convert.ToDouble(value);
            }
            catch
            {
                // ignored
            }
        }

        return 0;
    }
}

public sealed class MatchedEvent
{
    public string CameraName { get; set; } = string.Empty;
    public string CameraId { get; set; } = string.Empty;
    public Guid RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string TargetClass { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public List<Detection> Boxes { get; set; } = new();
}
