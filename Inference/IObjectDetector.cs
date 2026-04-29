using OpenCvSharp;

namespace KonturVideo.AI.Inference;

public interface IObjectDetector
{
    IReadOnlyList<Detection> Detect(Mat frame, CancellationToken cancellationToken = default);
}
