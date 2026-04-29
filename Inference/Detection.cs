namespace KonturVideo.AI.Inference;

public sealed class Detection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = string.Empty;
    public int ClassId { get; set; }
    public float Confidence { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }

    public float CenterX => X + (W / 2f);
    public float CenterY => Y + (H / 2f);
    public float AreaPercent => W * H * 100f;
}
