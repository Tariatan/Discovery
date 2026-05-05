using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace Automaton;

internal sealed class ScreenCaptureService
{
    private const string CapturesFolderName = "captures";
    private const string CaptureFilePrefix = "capture-";
    private const string CaptureTimestampFormat = "yyyyMMdd-HHmmss";
    private const int MinimumCaptureDimension = 1;
    private const int GameCaptureLeft = 0;
    private const int GameCaptureTop = 0;
    private const int GameCaptureWidth = 2_560;
    private const int GameCaptureHeight = 2_160;
    private const int VirtualScreenLeftMetric = 76;
    private const int VirtualScreenTopMetric = 77;
    private const int VirtualScreenWidthMetric = 78;
    private const int VirtualScreenHeightMetric = 79;
    private static readonly ILogger Logger = Log.ForContext<ScreenCaptureService>();

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
        var summary = m_SampleImageProcessor.ProcessSamples();
        Logger.Information(
            "Processed samples from screen capture service. SamplesDirectory={SamplesDirectory}, ResultCount={ResultCount}",
            summary.SamplesDirectory,
            summary.Results.Count);
    }

    public ScreenCaptureSummary CaptureAndProcessCurrentScreen()
    {
        var analysis = CaptureAndAnalyzeCurrentScreen();
        return new ScreenCaptureSummary(analysis.CapturesDirectory, analysis.CapturePath, analysis.Analysis.Result);
    }

    internal ScreenCaptureAnalysisSummary CaptureAndAnalyzeCurrentScreen()
    {
        // Persist the captured desktop first so the exact input remains available
        // for debugging, then process it through the same screenshot pipeline.
        var capturePath = CaptureCurrentScreenTrace();
        var analysis = m_SampleImageProcessor.AnalyzeImageFile(capturePath);
        Logger.Information(
            "Captured and analyzed current screen. CapturePath={CapturePath}, PlayfieldFound={PlayfieldFound}, ClusterCount={ClusterCount}, OutputPath={OutputPath}",
            capturePath,
            analysis.Result.PlayfieldFound,
            analysis.Result.ClusterCount,
            analysis.Result.OutputPath);

        return new ScreenCaptureAnalysisSummary(m_CapturesDirectory, capturePath, analysis);
    }

    internal string CaptureCurrentScreenTrace(string suffix = "")
    {
        Directory.CreateDirectory(m_CapturesDirectory);
        var capturePath = Path.Combine(
            m_CapturesDirectory,
            $"{CaptureFilePrefix}{DateTime.Now.ToString(CaptureTimestampFormat)}{suffix}.png");
        CaptureCurrentScreenToFile(capturePath);
        Logger.Information("Captured current screen trace. CapturePath={CapturePath}", capturePath);
        return capturePath;
    }

    internal void CaptureCurrentScreenToFile(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        m_ScreenCaptureProvider.CaptureToFile(outputPath);
        Logger.Debug("Captured current screen to file. OutputPath={OutputPath}", outputPath);
    }

    internal SampleImageAnalysisResult AnalyzeImageFile(string imagePath, bool writeAnnotatedOutput = true)
    {
        return m_SampleImageProcessor.AnalyzeImageFile(imagePath, writeAnnotatedOutput);
    }

    internal static Rectangle BuildGameCaptureBounds(Rectangle virtualScreenBounds)
    {
        var gameBounds = new Rectangle(
            GameCaptureLeft,
            GameCaptureTop,
            GameCaptureWidth,
            GameCaptureHeight);
        var captureBounds = Rectangle.Intersect(virtualScreenBounds, gameBounds);
        if (captureBounds.Width < MinimumCaptureDimension ||
            captureBounds.Height < MinimumCaptureDimension)
        {
            return virtualScreenBounds;
        }

        return captureBounds;
    }

    internal interface IScreenCaptureProvider
    {
        void CaptureToFile(string outputPath);
    }

    private sealed class ScreenCaptureProvider : IScreenCaptureProvider
    {
        public void CaptureToFile(string outputPath)
        {
            var bounds = GetPhysicalGameCaptureBounds();

            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            bitmap.Save(outputPath, ImageFormat.Png);
        }

        private static Rectangle GetPhysicalGameCaptureBounds()
        {
            return BuildGameCaptureBounds(GetPhysicalVirtualScreenBounds());
        }

        private static Rectangle GetPhysicalVirtualScreenBounds()
        {
            return new Rectangle(
                GetSystemMetrics(VirtualScreenLeftMetric),
                GetSystemMetrics(VirtualScreenTopMetric),
                Math.Max(MinimumCaptureDimension, GetSystemMetrics(VirtualScreenWidthMetric)),
                Math.Max(MinimumCaptureDimension, GetSystemMetrics(VirtualScreenHeightMetric)));
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
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
