using Microsoft.Extensions.Options;
using OpenCvSharp;
using System.Diagnostics;
using System.Text;

namespace KonturVideo.AI.Services;

public sealed class FrameSampler
{
    private readonly AiOptions _options;
    private readonly object _sync = new();

    private Process? _ffmpeg;
    private Thread? _readerThread;
    private volatile bool _readerStopRequested;

    private Mat? _latestFrame;
    private DateTime _lastFrameUtc;
    private int _frameWidth;
    private int _frameHeight;

    public DateTime LastFrameUtc
    {
        get
        {
            lock (_sync)
            {
                return _lastFrameUtc;
            }
        }
    }

    public bool IsRunning => _ffmpeg != null && !_ffmpeg.HasExited;

    public FrameSampler(IOptions<AiOptions> options)
    {
        _options = options.Value;
    }

    public bool TryOpen(string rtspUrl, out object handle)
    {
        handle = null!;

        Close();

        try
        {
            _frameWidth = 512;
            _frameHeight = 288;
            _readerStopRequested = false;

            _ffmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = string.IsNullOrWhiteSpace(_options.FfmpegPath)
                        ? @"C:\ffmpeg\bin\ffmpeg.exe"
                        : _options.FfmpegPath,
                    Arguments =
                        $"-loglevel error -nostats " +
                        "-rtsp_transport tcp " +
                        "-fflags nobuffer " +
                        "-flags low_delay " +
                        "-analyzeduration 0 " +
                        "-probesize 32 " +
                        "-avioflags direct " +
                        $"-i \"{rtspUrl}\" " +
                        //$"-vf fps={Math.Max(1, _options.FramesPerSecond)},scale={_frameWidth}:{_frameHeight} " +
                        $"-vf scale={_frameWidth}:{_frameHeight} " +
                        "-an -sn -dn " +
                        "-pix_fmt bgr24 " +
                        "-vcodec rawvideo " +
                        "-f rawvideo pipe:1",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true
                }
            };

            _ffmpeg.Start();
            Thread.Sleep(300);

            if (_ffmpeg.HasExited)
            {
                var error = SafeReadStandardError(_ffmpeg);
                Console.WriteLine("FFmpeg exited early: " + error);
                Close();
                return false;
            }

            _readerThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "FrameSamplerReader"
            };

            _readerThread.Start();
            handle = this;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FFmpeg open failed: " + ex);
            Close();
            return false;
        }
    }

    public bool TryReadFrame(out Mat frame)
    {
        frame = new Mat();

        try
        {
            lock (_sync)
            {
                if (_latestFrame == null || _latestFrame.Empty())
                {
                    frame.Dispose();
                    return false;
                }

                frame = _latestFrame.Clone();
                return !frame.Empty();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("TryReadFrame failed: " + ex.Message);
            frame.Dispose();
            return false;
        }
    }

    private void ReadLoop()
    {
        var process = _ffmpeg;
        if (process == null)
            return;

        var stream = process.StandardOutput.BaseStream;
        var bytesNeeded = _frameWidth * _frameHeight * 3;
        var buffer = new byte[bytesNeeded];

        try
        {
            while (!_readerStopRequested && !process.HasExited)
            {
                var read = 0;

                while (read < bytesNeeded && !_readerStopRequested && !process.HasExited)
                {
                    var n = stream.Read(buffer, read, bytesNeeded - read);
                    if (n <= 0)
                    {
                        return;
                    }

                    read += n;
                }

                if (_readerStopRequested || process.HasExited)
                    return;

                var mat = new Mat(_frameHeight, _frameWidth, MatType.CV_8UC3);
                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, mat.Data, bytesNeeded);

                if (mat.Empty())
                {
                    mat.Dispose();
                    continue;
                }

                lock (_sync)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = mat;
                    _lastFrameUtc = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FrameSampler read loop failed: " + ex.Message);

            try
            {
                if (process is { HasExited: true })
                {
                    var error = SafeReadStandardError(process);
                    if (!string.IsNullOrWhiteSpace(error))
                        Console.WriteLine("FFmpeg stderr after read loop failure: " + error);
                }
            }
            catch
            {
            }
        }
    }

    public void Close()
    {
        _readerStopRequested = true;

        try
        {
            if (_readerThread != null && _readerThread.IsAlive)
                _readerThread.Join(500);
        }
        catch
        {
        }

        _readerThread = null;

        try
        {
            if (_ffmpeg != null && !_ffmpeg.HasExited)
            {
                _ffmpeg.Kill(true);
                _ffmpeg.WaitForExit(1000);
            }
        }
        catch
        {
        }

        try
        {
            if (_ffmpeg != null)
            {
                var error = SafeReadStandardError(_ffmpeg);
                if (!string.IsNullOrWhiteSpace(error))
                    Console.WriteLine("FFmpeg stderr on close: " + error);
            }
        }
        catch
        {
        }

        try
        {
            _ffmpeg?.Dispose();
        }
        catch
        {
        }

        _ffmpeg = null;

        lock (_sync)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
            _lastFrameUtc = default;
        }
    }

    private static string SafeReadStandardError(Process process)
    {
        try
        {
            return process.StandardError.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }
}