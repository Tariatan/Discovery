using OpenCvSharp;
using System.Runtime.InteropServices;

namespace Discovery;

internal sealed class AutomationService
{
    private const int StartupDelayMilliseconds = 1_000;
    private const int MinimumClickDelayMilliseconds = 300;
    private const int MaximumClickDelayMilliseconds = 800;
    private const int MouseDownDurationMilliseconds = 250;
    private const int HoverDelayMilliseconds = 200;
    private const int HoverWigglePixels = 3;
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

    public AutomationSummary AutomateCurrentScreen(System.Windows.DpiScale dpi)
    {
        m_AutomationInputController.Delay(StartupDelayMilliseconds);

        var captureSummary = m_ScreenCaptureService.CaptureAndAnalyzeCurrentScreen();
        var clickedPointCount = ClickPolygonPoints(captureSummary.Analysis.Polygons);

        m_AutomationInputController.Delay(MinimumClickDelayMilliseconds);

        // Focus the known safe control button area, but leave the actual confirmation
        // click as an explicit future step.
        FocusControlButton(ControlButtonBounds, dpi);

        return new AutomationSummary(captureSummary, clickedPointCount, ControlButtonBounds);
    }

    private int ClickPolygonPoints(IReadOnlyList<Point[]> polygons)
    {
        var clickedPointCount = 0;

        foreach (var polygon in polygons)
        {
            if (polygon.Length == 0)
            {
                continue;
            }

            foreach (var point in polygon)
            {
                m_AutomationInputController.MoveTo(point);
                m_AutomationInputController.LeftClick();
                clickedPointCount++;
                m_AutomationInputController.Delay(m_Random.Next(MinimumClickDelayMilliseconds, MaximumClickDelayMilliseconds + 1));
            }

            m_AutomationInputController.MoveTo(polygon[0]);
            m_AutomationInputController.LeftClick();
            clickedPointCount++;
            m_AutomationInputController.Delay(m_Random.Next(MinimumClickDelayMilliseconds, MaximumClickDelayMilliseconds + 1));
        }

        return clickedPointCount;
    }

    private void FocusControlButton(Rect controlButtonBounds, System.Windows.DpiScale dpi)
    {
        var anchor = new Point(
            m_Random.Next(controlButtonBounds.X, controlButtonBounds.Right),
            m_Random.Next(controlButtonBounds.Y, controlButtonBounds.Bottom));
        var scaledAnchor = ScalePointForDpi(anchor, dpi);

        m_AutomationInputController.MoveTo(scaledAnchor);
        m_AutomationInputController.Delay(HoverDelayMilliseconds);
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

        void LeftClick();

        void Delay(int milliseconds);
    }

    private sealed class AutomationInputController : IAutomationInputController
    {
        private const uint LeftDownEvent = 0x0002;
        private const uint LeftUpEvent = 0x0004;

        public void MoveTo(Point point)
        {
            SetCursorPos(point.X, point.Y);
        }

        public void LeftClick()
        {
            Thread.Sleep(MouseDownDurationMilliseconds);
            mouse_event(LeftDownEvent, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(MouseDownDurationMilliseconds);
            mouse_event(LeftUpEvent, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(MouseDownDurationMilliseconds);
        }

        public void Delay(int milliseconds)
        {
            Thread.Sleep(milliseconds);
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
    Rect? ControlButtonBounds);
