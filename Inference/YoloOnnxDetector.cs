using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace KonturVideo.AI.Inference;

public sealed class YoloOnnxDetector : IObjectDetector, IDisposable
{
    private readonly InferenceSession _session;
    private readonly ILogger<YoloOnnxDetector> _logger;
    private readonly int _imageSize;
    private readonly string[] _labels =
    {
        "person","bicycle","car","motorcycle","airplane","bus","train","truck","boat","traffic light",
        "fire hydrant","stop sign","parking meter","bench","bird","cat","dog","horse","sheep","cow",
        "elephant","bear","zebra","giraffe","backpack","umbrella","handbag","tie","suitcase","frisbee",
        "skis","snowboard","sports ball","kite","baseball bat","baseball glove","skateboard","surfboard","tennis racket","bottle",
        "wine glass","cup","fork","knife","spoon","bowl","banana","apple","sandwich","orange",
        "broccoli","carrot","hot dog","pizza","donut","cake","chair","couch","potted plant","bed",
        "dining table","toilet","tv","laptop","mouse","remote","keyboard","cell phone","microwave","oven",
        "toaster","sink","refrigerator","book","clock","vase","scissors","teddy bear","hair drier","toothbrush"
    };

    public YoloOnnxDetector(IOptions<AiOptions> options, ILogger<YoloOnnxDetector> logger)
    {
        _logger = logger;
        var opt = options.Value;
        _imageSize = opt.ImageSize;

        var sessionOptions = new SessionOptions();
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        var mode = "CPU";

        if (opt.UseGpu)
        {
            try
            {
                sessionOptions.AppendExecutionProvider_CUDA();
                mode = "CUDA";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CUDA not available");
            }
        }

        if (mode == "CPU" && opt.UseDirectMl)
        {
            try
            {
                // Выбор номера видеоадаптера
                sessionOptions.AppendExecutionProvider_DML(opt.GpuDeviceId);
                mode = "DirectML";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DirectML not available");
            }
        }

        _session = new InferenceSession(opt.ModelPath, sessionOptions);
        _logger.LogInformation("ONNX initialized using {Mode}", mode);
    }

    public IReadOnlyList<Detection> Detect(Mat frame, CancellationToken cancellationToken = default)
    {
        using var resized = Letterbox(frame, _imageSize, out var scale, out var padX, out var padY);
        var tensor = ImageToTensor(resized);
        var inputName = _session.InputNames[0];

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, tensor)
        };
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);

        var output = results.First().AsTensor<float>();
        var dims = output.Dimensions.ToArray();

        // common YOLOv8 export: [1,84,8400]
        var detections = dims.Length == 3 ? ParseYoloV8(output, dims, frame.Width, frame.Height, scale, padX, padY) : new List<Detection>();
        return Nms(detections, 0.45f);
    }

    private List<Detection> ParseYoloV8(Tensor<float> output, int[] dims, int originalWidth, int originalHeight, float scale, int padX, int padY)
    {
        var channels = dims[1];
        var count = dims[2];
        if (channels < 5)
            return new();

        var classCount = channels - 4;
        var list = new List<Detection>(Math.Min(count, 256));

        for (var i = 0; i < count; i++)
        {
            var cx = output[0, 0, i];
            var cy = output[0, 1, i];
            var w = output[0, 2, i];
            var h = output[0, 3, i];

            var bestClass = -1;
            var bestScore = 0f;
            for (var c = 0; c < classCount; c++)
            {
                var score = output[0, 4 + c, i];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestClass = c;
                }
            }

            if (bestScore < 0.25f || bestClass < 0)
                continue;

            var x1 = cx - (w / 2f);
            var y1 = cy - (h / 2f);
            var x2 = cx + (w / 2f);
            var y2 = cy + (h / 2f);

            x1 = (x1 - padX) / scale;
            x2 = (x2 - padX) / scale;
            y1 = (y1 - padY) / scale;
            y2 = (y2 - padY) / scale;

            x1 = Math.Clamp(x1, 0, originalWidth - 1);
            x2 = Math.Clamp(x2, 0, originalWidth - 1);
            y1 = Math.Clamp(y1, 0, originalHeight - 1);
            y2 = Math.Clamp(y2, 0, originalHeight - 1);

            var bw = Math.Max(0, x2 - x1);
            var bh = Math.Max(0, y2 - y1);
            if (bw < 2 || bh < 2)
                continue;

            list.Add(new Detection
            {
                ClassId = bestClass,
                Label = bestClass >= 0 && bestClass < _labels.Length ? _labels[bestClass] : $"class-{bestClass}",
                Confidence = bestScore,
                X = x1 / originalWidth,
                Y = y1 / originalHeight,
                W = bw / originalWidth,
                H = bh / originalHeight
            });
        }

        return list;
    }

    private static DenseTensor<float> ImageToTensor(Mat image)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(image, rgb, ColorConversionCodes.BGR2RGB);

        var tensor = new DenseTensor<float>(new[] { 1, 3, rgb.Rows, rgb.Cols });
        for (var y = 0; y < rgb.Rows; y++)
        {
            for (var x = 0; x < rgb.Cols; x++)
            {
                var pixel = rgb.At<Vec3b>(y, x);
                tensor[0, 0, y, x] = pixel.Item0 / 255f;
                tensor[0, 1, y, x] = pixel.Item1 / 255f;
                tensor[0, 2, y, x] = pixel.Item2 / 255f;
            }
        }
        return tensor;
    }

    private static Mat Letterbox(Mat source, int targetSize, out float scale, out int padX, out int padY)
    {
        var srcW = source.Width;
        var srcH = source.Height;
        scale = Math.Min((float)targetSize / srcW, (float)targetSize / srcH);

        var newW = (int)Math.Round(srcW * scale);
        var newH = (int)Math.Round(srcH * scale);

        using var resized = new Mat();
        Cv2.Resize(source, resized, new Size(newW, newH));

        var output = new Mat(new Size(targetSize, targetSize), MatType.CV_8UC3, new Scalar(114, 114, 114));
        padX = (targetSize - newW) / 2;
        padY = (targetSize - newH) / 2;

        var roi = new Rect(padX, padY, newW, newH);
        resized.CopyTo(new Mat(output, roi));
        return output;
    }

    private static IReadOnlyList<Detection> Nms(List<Detection> detections, float iouThreshold)
    {
        var ordered = detections.OrderByDescending(x => x.Confidence).ToList();
        var kept = new List<Detection>();

        while (ordered.Count > 0)
        {
            var current = ordered[0];
            ordered.RemoveAt(0);
            kept.Add(current);

            ordered.RemoveAll(other =>
                other.ClassId == current.ClassId &&
                IoU(current, other) > iouThreshold);
        }

        return kept;
    }

    private static float IoU(Detection a, Detection b)
    {
        var ax2 = a.X + a.W;
        var ay2 = a.Y + a.H;
        var bx2 = b.X + b.W;
        var by2 = b.Y + b.H;

        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(ax2, bx2);
        var y2 = Math.Min(ay2, by2);

        var interW = Math.Max(0, x2 - x1);
        var interH = Math.Max(0, y2 - y1);
        var inter = interW * interH;
        if (inter <= 0) return 0;

        var union = (a.W * a.H) + (b.W * b.H) - inter;
        return union <= 0 ? 0 : inter / union;
    }

    public void Dispose() => _session.Dispose();
}
