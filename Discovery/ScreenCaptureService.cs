using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace Discovery;

internal sealed class ScreenCaptureService
{
    private const string CapturesFolderName = "captures";
    private const string CaptureFilePrefix = "capture-";
    private const string CaptureTimestampFormat = "yyyyMMdd-HHmmss";
    private const int MinimumCaptureDimension = 1;

    private readonly IScreenCaptureProvider m_ScreenCaptureProvider;
    private readonly SampleImageProcessor m_SampleImageProcessor;
    private readonly string m_CapturesDirectory;

    public ScreenCaptureService()
        : this(new ScreenCaptureProvider(), new SampleImageProcessor())
    {
    }

    internal ScreenCaptureService(
        IScreenCaptureProvider screenCaptureProvider,
        SampleImageProcessor sampleImageProcessor)
    {
        m_ScreenCaptureProvider = screenCaptureProvider;
        m_SampleImageProcessor = sampleImageProcessor;
        m_CapturesDirectory = CapturesFolderName;
    }

    public void ProcessSamples()
    {
        m_SampleImageProcessor.ProcessSamples();
    }

    public ScreenCaptureSummary CaptureAndProcessCurrentScreen()
    {
        var analysis = CaptureAndAnalyzeCurrentScreen();
        return new ScreenCaptureSummary(analysis.CapturesDirectory, analysis.CapturePath, analysis.Analysis.Result);
    }

    internal ScreenCaptureAnalysisSummary CaptureAndAnalyzeCurrentScreen()
    {
        Directory.CreateDirectory(m_CapturesDirectory);

        var capturePath = Path.Combine(
            m_CapturesDirectory,
            $"{CaptureFilePrefix}{DateTime.Now.ToString(CaptureTimestampFormat)}.png");

        // Persist the captured desktop first so the exact input remains available
        // for debugging, then process it through the same screenshot pipeline.
        CaptureCurrentScreenToFile(capturePath);
        var analysis = m_SampleImageProcessor.AnalyzeImageFile(capturePath);

        return new ScreenCaptureAnalysisSummary(m_CapturesDirectory, capturePath, analysis);
    }

    internal void CaptureCurrentScreenToFile(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        m_ScreenCaptureProvider.CaptureToFile(outputPath);
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
    string CapturePath,
    SampleProcessingResult Result);

internal sealed record ScreenCaptureAnalysisSummary(
    string CapturesDirectory,
    string CapturePath,
    SampleImageAnalysisResult Analysis);
