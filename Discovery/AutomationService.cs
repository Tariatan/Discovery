using OpenCvSharp;
using System.IO;
using System.Runtime.InteropServices;

namespace Discovery;

internal sealed class AutomationService
{
    private const int StartupDelayMilliseconds = 3_000;
    private const int MaximumSubmissionsPerWindow = 5;
    private const int SubmissionWindowMilliseconds = 70_000;
    private const int MinimumClickDelayMilliseconds = 300;
    private const int MaximumClickDelayMilliseconds = 800;
    private const int MouseDownDurationMilliseconds = 250;
    private const int AfterSubmitDelayMilliseconds = 4_000;
    private const int HoverDelayMilliseconds = 200;
    private static readonly Rect ControlButtonBounds = new(930, 645, 271, 11);

    private readonly ScreenCaptureService m_ScreenCaptureService;
    private readonly IAutomationInputController m_AutomationInputController;
    private readonly IAutomationClock m_AutomationClock;
    private readonly MaximumSubmissionsPopupDetector m_MaximumSubmissionsPopupDetector;
    private readonly Random m_Random = new();

    public AutomationService()
        : this(new ScreenCaptureService(), new AutomationInputController(), new SystemAutomationClock())
    {
    }

    internal AutomationService(ScreenCaptureService screenCaptureService, IAutomationInputController automationInputController)
        : this(screenCaptureService, automationInputController, new SystemAutomationClock())
    {
    }

    internal AutomationService(
        ScreenCaptureService screenCaptureService,
        IAutomationInputController automationInputController,
        IAutomationClock automationClock)
    {
        m_ScreenCaptureService = screenCaptureService;
        m_AutomationInputController = automationInputController;
        m_AutomationClock = automationClock;
        m_MaximumSubmissionsPopupDetector = new MaximumSubmissionsPopupDetector();
    }

    public void ProcessSamples()
    {
        m_ScreenCaptureService.ProcessSamples();
    }

    public AutomationSummary AutomateCurrentScreen(System.Windows.DpiScale dpi, CancellationToken cancellationToken)
    {
        m_AutomationInputController.Delay(StartupDelayMilliseconds, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        AutomationSummary? lastSummary = null;
        var rateLimiter = new AutomationSubmitRateLimiter();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                lastSummary = AutomateSingleCycle(dpi, rateLimiter, cancellationToken);
                if (lastSummary.MaximumSubmissionsReached)
                {
                    return lastSummary;
                }

                m_AutomationInputController.Delay(MinimumClickDelayMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (lastSummary is not null)
        {
            return lastSummary;
        }

        return lastSummary ?? throw new OperationCanceledException(cancellationToken);
    }

    private AutomationSummary AutomateSingleCycle(
        System.Windows.DpiScale dpi,
        AutomationSubmitRateLimiter rateLimiter,
        CancellationToken cancellationToken)
    {
        var captureSummary = m_ScreenCaptureService.CaptureAndAnalyzeCurrentScreen();
        cancellationToken.ThrowIfCancellationRequested();
        var clickedPointCount = ClickPolygonPoints(captureSummary.Analysis.Polygons, cancellationToken);

        m_AutomationInputController.Delay(MinimumClickDelayMilliseconds, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Focus the known safe control button area.
        FocusControlButton(ControlButtonBounds, dpi, cancellationToken);

        DelayBeforeRateLimitedSubmit(rateLimiter, cancellationToken);

        // Left-click the 'Submit' button.
        m_AutomationInputController.LeftClick(cancellationToken);
        rateLimiter.RecordSubmit(m_AutomationClock.UtcNow);
        m_AutomationInputController.Delay(AfterSubmitDelayMilliseconds, cancellationToken);
        var focusedCapturePath = CaptureFocusedScreenTrace(captureSummary, cancellationToken);
        if (m_MaximumSubmissionsPopupDetector.DetectAndDrawDebugOverlay(focusedCapturePath))
        {
            return new AutomationSummary(
                captureSummary,
                clickedPointCount,
                ControlButtonBounds,
                focusedCapturePath,
                MaximumSubmissionsReached: true);
        }

        // Left-click the 'Continue' button.
        m_AutomationInputController.LeftClick(cancellationToken);
        m_AutomationInputController.Delay(MinimumClickDelayMilliseconds, cancellationToken);

        // Left-click the next 'Continue' button.
        m_AutomationInputController.LeftClick(cancellationToken);

        return new AutomationSummary(captureSummary, clickedPointCount, ControlButtonBounds, focusedCapturePath);
    }

    private void DelayBeforeRateLimitedSubmit(AutomationSubmitRateLimiter rateLimiter, CancellationToken cancellationToken)
    {
        var delay = rateLimiter.GetDelayBeforeNextSubmit(m_AutomationClock.UtcNow);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        m_AutomationInputController.Delay((int)Math.Ceiling(delay.TotalMilliseconds), cancellationToken);
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

    internal interface IAutomationClock
    {
        DateTime UtcNow { get; }
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
            var leftButtonPressed = false;

            try
            {
                mouse_event(LeftDownEvent, 0, 0, 0, UIntPtr.Zero);
                leftButtonPressed = true;
                Thread.Sleep(MouseDownDurationMilliseconds);
            }
            finally
            {
                if (leftButtonPressed)
                {
                    mouse_event(LeftUpEvent, 0, 0, 0, UIntPtr.Zero);
                }
            }

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

    private sealed class SystemAutomationClock : IAutomationClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private sealed class AutomationSubmitRateLimiter
    {
        private readonly Queue<DateTime> m_SubmittedAtUtc = new();

        public TimeSpan GetDelayBeforeNextSubmit(DateTime utcNow)
        {
            RemoveExpiredSubmissions(utcNow);
            if (m_SubmittedAtUtc.Count < MaximumSubmissionsPerWindow)
            {
                return TimeSpan.Zero;
            }

            var elapsed = utcNow - m_SubmittedAtUtc.Peek();
            var remaining = TimeSpan.FromMilliseconds(SubmissionWindowMilliseconds) - elapsed;
            return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        public void RecordSubmit(DateTime utcNow)
        {
            RemoveExpiredSubmissions(utcNow);
            m_SubmittedAtUtc.Enqueue(utcNow);
        }

        private void RemoveExpiredSubmissions(DateTime utcNow)
        {
            while (m_SubmittedAtUtc.Count > 0 &&
                   (utcNow - m_SubmittedAtUtc.Peek()).TotalMilliseconds >= SubmissionWindowMilliseconds)
            {
                m_SubmittedAtUtc.Dequeue();
            }
        }
    }
}

internal sealed record AutomationSummary(
    ScreenCaptureAnalysisSummary CaptureSummary,
    int ClickedPointCount,
    Rect? ControlButtonBounds,
    string FocusedCapturePath,
    bool MaximumSubmissionsReached = false);
