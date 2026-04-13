using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace Discovery;

internal sealed class ScreenCaptureService
{
    private const string CapturesFolderName = "captures";
    private const string CaptureOutputFolderName = "output";
    private const string CaptureFilePrefix = "capture-";
    private const string CaptureTimestampFormat = "yyyyMMdd-HHmmss";
    private const int MinimumCaptureDimension = 1;

    private readonly IScreenCaptureProvider m_ScreenCaptureProvider;
    private readonly SampleImageProcessor m_SampleImageProcessor;

    public ScreenCaptureService()
        : this(new ScreenCaptureProvider(), new SampleImageProcessor())
    {
    }

    internal ScreenCaptureService(IScreenCaptureProvider screenCaptureProvider, SampleImageProcessor sampleImageProcessor)
    {
        m_ScreenCaptureProvider = screenCaptureProvider;
        m_SampleImageProcessor = sampleImageProcessor;
    }

    public ScreenCaptureSummary CaptureAndProcessCurrentScreen(string projectRoot)
    {
        var capturesDirectory = Path.Combine(projectRoot, CapturesFolderName);
        var outputDirectory = Path.Combine(capturesDirectory, CaptureOutputFolderName);
        Directory.CreateDirectory(capturesDirectory);

        var capturePath = Path.Combine(
            capturesDirectory,
            $"{CaptureFilePrefix}{DateTime.Now.ToString(CaptureTimestampFormat)}.png");

        // Persist the captured desktop first so the exact input remains available
        // for debugging, then process it through the same screenshot pipeline.
        m_ScreenCaptureProvider.CaptureToFile(capturePath);
        var result = m_SampleImageProcessor.ProcessImageFile(capturePath, outputDirectory);

        return new ScreenCaptureSummary(capturesDirectory, outputDirectory, capturePath, result);
    }

    internal interface IScreenCaptureProvider
    {
        void CaptureToFile(string outputPath);
    }

    private sealed class ScreenCaptureProvider : IScreenCaptureProvider
    {
        public void CaptureToFile(string outputPath)
        {
            var left = (int)Math.Floor(SystemParameters.VirtualScreenLeft);
            var top = (int)Math.Floor(SystemParameters.VirtualScreenTop);
            var width = Math.Max(MinimumCaptureDimension, (int)Math.Ceiling(SystemParameters.VirtualScreenWidth));
            var height = Math.Max(MinimumCaptureDimension, (int)Math.Ceiling(SystemParameters.VirtualScreenHeight));

            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
            bitmap.Save(outputPath, ImageFormat.Png);
        }
    }
}

internal sealed record ScreenCaptureSummary(
    string CapturesDirectory,
    string OutputDirectory,
    string CapturePath,
    SampleProcessingResult Result);
