using System.Text.Json.Serialization;

namespace KonturVideo.AI.Models;

public sealed class AgentCameraDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("streamKey")] public string? StreamKey { get; set; }
    [JsonPropertyName("rtspUrl")] public string RtspUrl { get; set; } = string.Empty;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("online")] public bool Online { get; set; }
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("ownerUserId")] public string? OwnerUserId { get; set; }
}
