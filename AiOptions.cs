namespace KonturVideo.AI;

public sealed class AiOptions
{
    public string ModelPath { get; set; } = "ModelsAI/yolov8n.onnx";
    public int ImageSize { get; set; } = 640;
    public int MaxParallelCameras { get; set; } = 2;
    public int FramesPerSecond { get; set; } = 2;
    public bool OverlayEveryFrame { get; set; } = true;
    public int IdleDelayMs { get; set; } = 250;
    public int FrameTimeoutMs { get; set; } = 6000;

    // GPU
    public bool UseGpu { get; set; } = true;
    public bool UseDirectMl { get; set; } = true;
    public int GpuDeviceId { get; set; } = 0;

    public bool PreferMediaMtx { get; set; } = true;
    public string MediaMtxRtspBase { get; set; } = "rtsp://localhost:8554";
    public string FfmpegPath { get; set; } = @"C:\ffmpeg\bin\ffmpeg.exe";
}
