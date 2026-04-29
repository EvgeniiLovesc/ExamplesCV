namespace KonturVideo.AI;

public sealed class AgentApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5001";
    public string ApiKey { get; set; } = "";
    public string CamerasPath { get; set; } = "/api/cameras";
}
