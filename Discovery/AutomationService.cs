using OpenCvSharp;
using System.IO;
using System.Runtime.InteropServices;

namespace Discovery;

internal sealed class AutomationService
{
    private const int StartupDelayMilliseconds = 3_000;
    private const int MinimumClickDelayMilliseconds = 300;
    private const int MaximumClickDelayMilliseconds = 800;
    private const int MouseDownDurationMilliseconds = 250;
    private const int SubmitDelayMilliseconds = 3_500;
    private const int HoverDelayMilliseconds = 200;
    private static readonly Rect ControlButtonBounds = new(930, 645, 271, 11);

    private readonly ScreenCaptureService m_ScreenCaptureService;
    private readonly IAutomationInputController m_AutomationInputController;
    private readonly Random m_Random = new();

    public AutomationService()
        : this(new ScreenCaptureService(), new AutomationInputController())
    {
    }

    internal AutomationService(ScreenCaptureService screenCaptureService, IAutomationInputController automationInputController)
    {
        m_ScreenCaptureService = screenCaptureService;
        m_AutomationInputController = automationInputController;
    }

    public void ProcessSamples()
    {
        m_ScreenCaptureService.ProcessSamples();
    }

    public AutomationSummary AutomateCurrentScreen(System.Windows.DpiScale dpi, CancellationToken cancellationToken)
    {
        m_AutomationInputController.Delay(StartupDelayMilliseconds, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var captureSummary = m_ScreenCaptureService.CaptureAndAnalyzeCurrentScreen();
        cancellationToken.ThrowIfCancellationRequested();
        var clickedPointCount = ClickPolygonPoints(captureSummary.Analysis.Polygons, cancellationToken);

        m_AutomationInputController.Delay(MinimumClickDelayMilliseconds, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Focus the known safe control button area.
        FocusControlButton(ControlButtonBounds, dpi, cancellationToken);

        // Left-click the 'Submit' button.
        //m_AutomationInputController.LeftClick(cancellationToken);
        //m_AutomationInputController.Delay(SubmitDelayMilliseconds, cancellationToken);
        var focusedCapturePath = CaptureFocusedScreenTrace(captureSummary, cancellationToken);

        return new AutomationSummary(captureSummary, clickedPointCount, ControlButtonBounds, focusedCapturePath);
    }

    private int ClickPolygonPoints(IReadOnlyList<Point[]> polygons, CancellationToken cancellationToken)
    {
        var clickedPointCount = 0;

        foreach (var polygon in polygons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (polygon.Length == 0)
            {
                continue;
            }

            foreach (var point in polygon)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_AutomationInputController.MoveTo(point);
                m_AutomationInputController.LeftClick(cancellationToken);
                clickedPointCount++;
                m_AutomationInputController.Delay(m_Random.Next(MinimumClickDelayMilliseconds, MaximumClickDelayMilliseconds + 1), cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            m_AutomationInputController.MoveTo(polygon[0]);
            m_AutomationInputController.LeftClick(cancellationToken);
            clickedPointCount++;
            m_AutomationInputController.Delay(m_Random.Next(MinimumClickDelayMilliseconds, MaximumClickDelayMilliseconds + 1), cancellationToken);
        }

        return clickedPointCount;
    }

    private void FocusControlButton(Rect controlButtonBounds, System.Windows.DpiScale dpi, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var anchor = new Point(
            m_Random.Next(controlButtonBounds.X, controlButtonBounds.Right),
            m_Random.Next(controlButtonBounds.Y, controlButtonBounds.Bottom));
        var scaledAnchor = ScalePointForDpi(anchor, dpi);

        m_AutomationInputController.MoveTo(scaledAnchor);
        m_AutomationInputController.Delay(HoverDelayMilliseconds, cancellationToken);
    }

    private string CaptureFocusedScreenTrace(ScreenCaptureAnalysisSummary captureSummary, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var focusedCapturePath = Path.Combine(
            captureSummary.CapturesDirectory,
            $"{Path.GetFileNameWithoutExtension(captureSummary.CapturePath)}.focused.png");
        m_ScreenCaptureService.CaptureCurrentScreenToFile(focusedCapturePath);
        return focusedCapturePath;
    }

    internal static Point ScalePointForDpi(Point point, System.Windows.DpiScale dpi)
    {
        return new Point(
            (int)Math.Round(point.X * dpi.DpiScaleX, MidpointRounding.AwayFromZero),
            (int)Math.Round(point.Y * dpi.DpiScaleY, MidpointRounding.AwayFromZero));
    }

    internal interface IAutomationInputController
    {
        void MoveTo(Point point);

        void LeftClick(CancellationToken cancellationToken);

        void Delay(int milliseconds, CancellationToken cancellationToken);
    }

    private sealed class AutomationInputController : IAutomationInputController
    {
        private const uint LeftDownEvent = 0x0002;
        private const uint LeftUpEvent = 0x0004;

        public void MoveTo(Point point)
        {
            SetCursorPos(point.X, point.Y);
        }

        public void LeftClick(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(MouseDownDurationMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
            mouse_event(LeftDownEvent, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(MouseDownDurationMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
            mouse_event(LeftUpEvent, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(MouseDownDurationMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void Delay(int milliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.WaitHandle.WaitOne(milliseconds);
            cancellationToken.ThrowIfCancellationRequested();
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    }
}

internal sealed record AutomationSummary(
    ScreenCaptureAnalysisSummary CaptureSummary,
    int ClickedPointCount,
    Rect? ControlButtonBounds,
    string FocusedCapturePath);
