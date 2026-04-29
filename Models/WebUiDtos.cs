using System.Text.Json.Serialization;

namespace KonturVideo.AI.Models;

public sealed class LoginRequest
{
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;
}

public sealed class AiRuleDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("cameraName")] public string CameraName { get; set; } = string.Empty;
    [JsonPropertyName("cameraId")] public string? CameraId { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("eventType")] public string EventType { get; set; } = "object-detection";
    [JsonPropertyName("targetClass")] public string TargetClass { get; set; } = string.Empty;
    [JsonPropertyName("threshold")] public double Threshold { get; set; } = 0.65;
    [JsonPropertyName("cooldownSeconds")] public int CooldownSeconds { get; set; } = 15;
    [JsonPropertyName("mode")] public string Mode { get; set; } = string.Empty;
    [JsonPropertyName("severity")] public string Severity { get; set; } = "medium";
    [JsonPropertyName("filters")] public RuleFiltersDto Filters { get; set; } = new();
    [JsonPropertyName("region")] public RuleRegionDto Region { get; set; } = new();
    [JsonPropertyName("actions")] public RuleActionsDto Actions { get; set; } = new();
}

public sealed class RuleFiltersDto
{
    [JsonPropertyName("classesCsv")] public string ClassesCsv { get; set; } = string.Empty;
    [JsonPropertyName("minAreaPercent")] public double MinAreaPercent { get; set; } = 1;
    [JsonPropertyName("maxAreaPercent")] public double MaxAreaPercent { get; set; } = 90;
    [JsonPropertyName("minDurationMs")] public int MinDurationMs { get; set; } = 200;
    [JsonPropertyName("confidence")] public double Confidence { get; set; } = 0.65;
}

public sealed class RuleRegionDto
{
    [JsonPropertyName("polygon")] public List<GeoPointDto> Polygon { get; set; } = new();
    [JsonPropertyName("line")] public List<GeoPointDto> Line { get; set; } = new();
    [JsonPropertyName("direction")] public string Direction { get; set; } = "both";
}

public sealed class RuleActionsDto
{
    [JsonPropertyName("snapshot")] public bool Snapshot { get; set; }
    [JsonPropertyName("clip")] public bool Clip { get; set; }
    [JsonPropertyName("operatorPopup")] public bool OperatorPopup { get; set; }
    [JsonPropertyName("webhook")] public bool Webhook { get; set; }
    [JsonPropertyName("siren")] public bool Siren { get; set; }
    [JsonPropertyName("webhookUrl")] public string WebhookUrl { get; set; } = string.Empty;
}

public sealed class GeoPointDto
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
}

public sealed class OverlayIngestRequest
{
    [JsonPropertyName("cameraName")] public string CameraName { get; set; } = string.Empty;
    [JsonPropertyName("timestampUtc")] public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("frameCapturedAtUtc")] public DateTime? FrameCapturedAtUtc { get; set; }
    [JsonPropertyName("inferenceCompletedAtUtc")] public DateTime? InferenceCompletedAtUtc { get; set; }
    [JsonPropertyName("publishedAtUtc")] public DateTime? PublishedAtUtc { get; set; }

    [JsonPropertyName("boxes")] public List<OverlayBoxDto> Boxes { get; set; } = new();
}

public sealed class OverlayBoxDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    [JsonPropertyName("confidence")] public double Confidence { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("w")] public double W { get; set; }
    [JsonPropertyName("h")] public double H { get; set; }
}

public sealed class AiEventIngestRequest
{
    [JsonPropertyName("cameraName")] public string CameraName { get; set; } = string.Empty;
    [JsonPropertyName("cameraId")] public string? CameraId { get; set; }
    [JsonPropertyName("ruleId")] public Guid? RuleId { get; set; }
    [JsonPropertyName("ruleName")] public string RuleName { get; set; } = string.Empty;
    [JsonPropertyName("eventType")] public string EventType { get; set; } = string.Empty;
    [JsonPropertyName("targetClass")] public string TargetClass { get; set; } = string.Empty;
    [JsonPropertyName("severity")] public string Severity { get; set; } = "medium";
    [JsonPropertyName("timestampUtc")] public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("boxes")] public List<OverlayBoxDto> Boxes { get; set; } = new();
}
