using OpenCvSharp;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace Discovery;

internal sealed class AutomationService
{
    private const int StartupDelayMilliseconds = 3_000;
    private const int LauncherStartupDelayMilliseconds = 20_000;
    private const int MaximumSubmissionsPerWindow = 5;
    private const int SubmissionWindowMilliseconds = 90_000;
    private const int InitialPilotIndex = 1;
    private const int MinimumClickDelayMilliseconds = 300;
    private const int MaximumClickDelayMilliseconds = 800;
    private const int MouseDownDurationMilliseconds = 250;
    private const int AfterSubmitDelayMilliseconds = 4_000;
    private const int HoverDelayMilliseconds = 200;
    private const int PilotLogoutDelayMilliseconds = 1_000;
    private const int PilotSelectionConfirmDelayMilliseconds = 1_000;
    private const int PilotSelectionLoadDelayMilliseconds = 5_000;
    private const int PilotActivationDelayMilliseconds = 20_000;
    private const int FinalPilotLogoutConfirmDelayMilliseconds = 2_000;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyShift = 0x10;
    private const ushort VirtualKeyAlt = 0x12;
    private const ushort VirtualKeyEnter = 0x0D;
    private const ushort VirtualKeyL = 0x4C;
    private const ushort VirtualKeyQ = 0x51;
    private const ushort VirtualKeyW = 0x57;
    private const string NoPlayButtonFoundDebugText = "No play button found";
    private const string PilotNotFoundDebugTextTemplate = "Pilot {0} not found";
    private const double DebugOverlayTextScale = 0.8;
    private const int DebugOverlayTextThickness = 2;
    private const int DebugOverlayLeftPadding = 30;
    private const int DebugOverlayTopPadding = 40;
    private static readonly Rect ControlButtonBounds = new(930, 645, 271, 11);
    private static readonly Scalar DebugOverlayTextColor = new(80, 120, 255);
    private static readonly Serilog.ILogger Logger = Log.ForContext<AutomationService>();

    private readonly ScreenCaptureService m_ScreenCaptureService;
    private readonly IAutomationInputController m_AutomationInputController;
    private readonly IAutomationClock m_AutomationClock;
    private readonly MaximumSubmissionsPopupDetector m_MaximumSubmissionsPopupDetector;
    private readonly PilotAvatarLocator m_PilotAvatarLocator = new();
    private readonly PlayNowButtonLocator m_PlayNowButtonLocator = new();
    private readonly AutomationSubmitRateLimiter m_SubmitRateLimiter = new();
    private readonly Random m_Random = new();
    private int m_CurrentPilotIndex = InitialPilotIndex;

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
        Logger.Information("Processing samples through automation service.");
        m_ScreenCaptureService.ProcessSamples();
    }

    internal StartupAutomationSummary PrepareAutomationFromLauncherStartup(
        int initialPilotIndex,
        CancellationToken cancellationToken)
    {
        Logger.Information("Preparing launcher startup automation. InitialPilotIndex={InitialPilotIndex}", initialPilotIndex);
        var playButtonCapturePath = m_ScreenCaptureService.CaptureCurrentScreenTrace(".play");
        cancellationToken.ThrowIfCancellationRequested();

        using var playButtonScreen = Cv2.ImRead(playButtonCapturePath);
        if (!m_PlayNowButtonLocator.TryLocate(playButtonScreen, out var playButtonLocation))
        {
            DrawDebugOverlay(playButtonCapturePath, NoPlayButtonFoundDebugText);
            Logger.Warning("No play button found during startup automation. CapturePath={CapturePath}", playButtonCapturePath);
            return new StartupAutomationSummary(
                playButtonCapturePath,
                false,
                null);
        }

        Logger.Information(
            "Play button found during startup automation. CapturePath={CapturePath}, Bounds={Bounds}",
            playButtonCapturePath,
            playButtonLocation.Bounds);
        m_AutomationInputController.MoveTo(Center(playButtonLocation.Bounds));
        m_AutomationInputController.LeftClick(cancellationToken);
        m_AutomationInputController.Delay(LauncherStartupDelayMilliseconds, cancellationToken);
        m_AutomationInputController.PressKeyChord(VirtualKeyControl, VirtualKeyW, cancellationToken);

        var pilotSelectionCapturePath = m_ScreenCaptureService.CaptureCurrentScreenTrace($".startup-pilot-{initialPilotIndex}");
        cancellationToken.ThrowIfCancellationRequested();
        using var pilotSelectionScreen = Cv2.ImRead(pilotSelectionCapturePath);
        if (!m_PilotAvatarLocator.TryLocate(pilotSelectionScreen, initialPilotIndex, out var pilotLocation))
        {
            DrawPilotNotFoundDebugOverlay(pilotSelectionCapturePath, initialPilotIndex);
            Logger.Warning(
                "Pilot was not found during startup automation. PilotIndex={PilotIndex}, CapturePath={CapturePath}",
                initialPilotIndex,
                pilotSelectionCapturePath);
            return new StartupAutomationSummary(
                playButtonCapturePath,
                true,
                playButtonLocation.Bounds,
                pilotSelectionCapturePath);
        }

        Logger.Information(
            "Pilot found during startup automation. PilotIndex={PilotIndex}, CapturePath={CapturePath}, Bounds={Bounds}",
            initialPilotIndex,
            pilotSelectionCapturePath,
            pilotLocation.Bounds);
        m_AutomationInputController.MoveTo(Center(pilotLocation.Bounds));
        m_AutomationInputController.LeftClick(cancellationToken);
        m_AutomationInputController.Delay(LauncherStartupDelayMilliseconds, cancellationToken);
        m_CurrentPilotIndex = initialPilotIndex;
        m_AutomationInputController.PressKeyChord(VirtualKeyAlt, VirtualKeyL, cancellationToken);

        return new StartupAutomationSummary(
            playButtonCapturePath,
            true,
            playButtonLocation.Bounds,
            pilotSelectionCapturePath,
            true,
            pilotLocation.Bounds,
            true);
    }

    public AutomationSummary AutomateCurrentScreen(System.Windows.DpiScale dpi, CancellationToken cancellationToken)
    {
        return AutomateCurrentScreen(dpi, InitialPilotIndex, cancellationToken);
    }

    public AutomationSummary AutomateCurrentScreen(
        System.Windows.DpiScale dpi,
        int initialPilotIndex,
        CancellationToken cancellationToken)
    {
        Logger.Information("Automation loop starting. InitialPilotIndex={InitialPilotIndex}", initialPilotIndex);
        m_AutomationInputController.Delay(StartupDelayMilliseconds, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        AutomationSummary? lastSummary = null;
        m_CurrentPilotIndex = initialPilotIndex;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                lastSummary = AutomateSingleCycle(dpi, m_SubmitRateLimiter, cancellationToken);
                if (lastSummary is { MaximumSubmissionsReached: true, PilotSwitchSucceeded: false })
                {
                    Logger.Warning(
                        "Automation loop stopped because maximum submissions were reached and pilot switching did not succeed. CurrentPilotIndex={CurrentPilotIndex}, TargetPilotIndex={TargetPilotIndex}",
                        lastSummary.CurrentPilotIndex,
                        lastSummary.TargetPilotIndex);
                    return lastSummary;
                }

                m_AutomationInputController.Delay(MinimumClickDelayMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (lastSummary is not null)
        {
            Logger.Information(
                "Automation loop canceled after a completed cycle. CapturePath={CapturePath}, CurrentPilotIndex={CurrentPilotIndex}",
                lastSummary.CaptureSummary.CapturePath,
                lastSummary.CurrentPilotIndex);
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
        Logger.Information(
            "Automation cycle analyzed screen. CapturePath={CapturePath}, PlayfieldFound={PlayfieldFound}, ClusterCount={ClusterCount}, ClickedPointCount={ClickedPointCount}",
            captureSummary.CapturePath,
            captureSummary.Analysis.Result.PlayfieldFound,
            captureSummary.Analysis.Result.ClusterCount,
            clickedPointCount);

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
        var focusedCaptureAnalysis = m_ScreenCaptureService.AnalyzeImageFile(focusedCapturePath, writeAnnotatedOutput: false);
        if (!focusedCaptureAnalysis.Result.PlayfieldFound &&
            m_MaximumSubmissionsPopupDetector.DetectAndDrawDebugOverlay(focusedCapturePath))
        {
            var pilotSwitchResult = SwitchToNextPilot(captureSummary, cancellationToken);
            Logger.Warning(
                "Maximum submissions popup detected. FocusedCapturePath={FocusedCapturePath}, CurrentPilotIndex={CurrentPilotIndex}, TargetPilotIndex={TargetPilotIndex}, PilotSwitchSucceeded={PilotSwitchSucceeded}, PilotSwitchCapturePath={PilotSwitchCapturePath}",
                focusedCapturePath,
                m_CurrentPilotIndex,
                pilotSwitchResult.TargetPilotIndex,
                pilotSwitchResult.Succeeded,
                pilotSwitchResult.CapturePath);
            return new AutomationSummary(
                captureSummary,
                clickedPointCount,
                ControlButtonBounds,
                focusedCapturePath,
                MaximumSubmissionsReached: true,
                CurrentPilotIndex: m_CurrentPilotIndex,
                TargetPilotIndex: pilotSwitchResult.TargetPilotIndex,
                PilotSwitchSucceeded: pilotSwitchResult.Succeeded,
                PilotSwitchCapturePath: pilotSwitchResult.CapturePath);
        }

        // Left-click the 'Continue' button.
        m_AutomationInputController.LeftClick(cancellationToken);
        m_AutomationInputController.Delay(MinimumClickDelayMilliseconds, cancellationToken);

        // Left-click the next 'Continue' button.
        m_AutomationInputController.LeftClick(cancellationToken);
        Logger.Information(
            "Automation cycle submitted and continued. CapturePath={CapturePath}, FocusedCapturePath={FocusedCapturePath}, ClickedPointCount={ClickedPointCount}, CurrentPilotIndex={CurrentPilotIndex}",
            captureSummary.CapturePath,
            focusedCapturePath,
            clickedPointCount,
            m_CurrentPilotIndex);

        return new AutomationSummary(
            captureSummary,
            clickedPointCount,
            ControlButtonBounds,
            focusedCapturePath,
            CurrentPilotIndex: m_CurrentPilotIndex);
    }

    private PilotSwitchResult SwitchToNextPilot(
        ScreenCaptureAnalysisSummary captureSummary,
        CancellationToken cancellationToken)
    {
        if (!m_PilotAvatarLocator.TryGetNextPilotIndex(m_CurrentPilotIndex, out var nextPilotIndex))
        {
            Logger.Warning("No next pilot is configured. CurrentPilotIndex={CurrentPilotIndex}", m_CurrentPilotIndex);
            m_AutomationInputController.PressKeyChord(VirtualKeyAlt, VirtualKeyShift, VirtualKeyQ, cancellationToken);
            m_AutomationInputController.Delay(FinalPilotLogoutConfirmDelayMilliseconds, cancellationToken);
            m_AutomationInputController.PressKey(VirtualKeyEnter, cancellationToken);
            return new PilotSwitchResult(m_CurrentPilotIndex, Succeeded: false, null);
        }

        Logger.Information(
            "Switching pilot. CurrentPilotIndex={CurrentPilotIndex}, TargetPilotIndex={TargetPilotIndex}",
            m_CurrentPilotIndex,
            nextPilotIndex);
        m_AutomationInputController.Delay(PilotLogoutDelayMilliseconds, cancellationToken);
        m_AutomationInputController.PressKeyChord(VirtualKeyAlt, VirtualKeyQ, cancellationToken);
        m_AutomationInputController.Delay(PilotSelectionConfirmDelayMilliseconds, cancellationToken);
        m_AutomationInputController.PressKey(VirtualKeyEnter, cancellationToken);
        m_AutomationInputController.Delay(PilotSelectionLoadDelayMilliseconds, cancellationToken);

        var pilotSelectionCapturePath = CapturePilotSelectionScreenTrace(captureSummary, nextPilotIndex, cancellationToken);
        using var pilotSelectionScreen = Cv2.ImRead(pilotSelectionCapturePath);
        if (!m_PilotAvatarLocator.TryLocate(pilotSelectionScreen, nextPilotIndex, out var location))
        {
            DrawPilotNotFoundDebugOverlay(pilotSelectionCapturePath, nextPilotIndex);
            Logger.Warning(
                "Target pilot was not found. TargetPilotIndex={TargetPilotIndex}, CapturePath={CapturePath}",
                nextPilotIndex,
                pilotSelectionCapturePath);
            return new PilotSwitchResult(nextPilotIndex, Succeeded: false, pilotSelectionCapturePath);
        }

        m_AutomationInputController.MoveTo(Center(location.Bounds));
        m_AutomationInputController.LeftClick(cancellationToken);
        m_AutomationInputController.Delay(PilotActivationDelayMilliseconds, cancellationToken);
        m_CurrentPilotIndex = nextPilotIndex;
        m_AutomationInputController.PressKeyChord(VirtualKeyAlt, VirtualKeyL, cancellationToken);
        Logger.Information(
            "Pilot switch succeeded. CurrentPilotIndex={CurrentPilotIndex}, CapturePath={CapturePath}, Bounds={Bounds}",
            m_CurrentPilotIndex,
            pilotSelectionCapturePath,
            location.Bounds);
        return new PilotSwitchResult(nextPilotIndex, Succeeded: true, pilotSelectionCapturePath);
    }

    private void DelayBeforeRateLimitedSubmit(AutomationSubmitRateLimiter rateLimiter, CancellationToken cancellationToken)
    {
        var delay = rateLimiter.GetDelayBeforeNextSubmit(m_AutomationClock.UtcNow);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        Logger.Information("Waiting before submit because of rate limit. DelayMilliseconds={DelayMilliseconds}", (int)Math.Ceiling(delay.TotalMilliseconds));
        m_AutomationInputController.Delay((int)Math.Ceiling(delay.TotalMilliseconds), cancellationToken);
    }

    private int ClickPolygonPoints(IReadOnlyList<Point[]> polygons, CancellationToken cancellationToken)
    {
        Logger.Debug("Clicking polygon points. PolygonCount={PolygonCount}", polygons.Count);
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

        Logger.Debug("Finished clicking polygon points. ClickedPointCount={ClickedPointCount}", clickedPointCount);
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

    private string CapturePilotSelectionScreenTrace(
        ScreenCaptureAnalysisSummary captureSummary,
        int pilotIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pilotSelectionCapturePath = Path.Combine(
            captureSummary.CapturesDirectory,
            $"{Path.GetFileNameWithoutExtension(captureSummary.CapturePath)}.pilot-{pilotIndex}.png");
        m_ScreenCaptureService.CaptureCurrentScreenToFile(pilotSelectionCapturePath);
        return pilotSelectionCapturePath;
    }

    private static void DrawPilotNotFoundDebugOverlay(string imagePath, int pilotIndex)
    {
        DrawDebugOverlay(imagePath, string.Format(PilotNotFoundDebugTextTemplate, pilotIndex));
    }

    private static void DrawDebugOverlay(string imagePath, string text)
    {
        using var image = Cv2.ImRead(imagePath);
        if (image.Empty())
        {
            return;
        }

        Cv2.PutText(
            image,
            text,
            new Point(DebugOverlayLeftPadding, DebugOverlayTopPadding),
            HersheyFonts.HersheySimplex,
            DebugOverlayTextScale,
            DebugOverlayTextColor,
            DebugOverlayTextThickness,
            LineTypes.AntiAlias);
        Cv2.ImWrite(imagePath, image);
    }

    private static Point Center(Rect bounds)
    {
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
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

        void PressKey(ushort virtualKey, CancellationToken cancellationToken);

        void PressKeyChord(ushort modifierVirtualKey, ushort virtualKey, CancellationToken cancellationToken);

        void PressKeyChord(
            ushort firstModifierVirtualKey,
            ushort secondModifierVirtualKey,
            ushort virtualKey,
            CancellationToken cancellationToken);

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
        private const uint KeyUpEvent = 0x0002;

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

        public void PressKey(ushort virtualKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            keybd_event((byte)virtualKey, 0, 0, UIntPtr.Zero);
            Thread.Sleep(MouseDownDurationMilliseconds);
            keybd_event((byte)virtualKey, 0, KeyUpEvent, UIntPtr.Zero);
            Thread.Sleep(MouseDownDurationMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void PressKeyChord(ushort modifierVirtualKey, ushort virtualKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            keybd_event((byte)modifierVirtualKey, 0, 0, UIntPtr.Zero);

            try
            {
                keybd_event((byte)virtualKey, 0, 0, UIntPtr.Zero);
                Thread.Sleep(MouseDownDurationMilliseconds);
                keybd_event((byte)virtualKey, 0, KeyUpEvent, UIntPtr.Zero);
            }
            finally
            {
                keybd_event((byte)modifierVirtualKey, 0, KeyUpEvent, UIntPtr.Zero);
            }

            Thread.Sleep(MouseDownDurationMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void PressKeyChord(
            ushort firstModifierVirtualKey,
            ushort secondModifierVirtualKey,
            ushort virtualKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            keybd_event((byte)firstModifierVirtualKey, 0, 0, UIntPtr.Zero);
            keybd_event((byte)secondModifierVirtualKey, 0, 0, UIntPtr.Zero);

            try
            {
                keybd_event((byte)virtualKey, 0, 0, UIntPtr.Zero);
                Thread.Sleep(MouseDownDurationMilliseconds);
                keybd_event((byte)virtualKey, 0, KeyUpEvent, UIntPtr.Zero);
            }
            finally
            {
                keybd_event((byte)secondModifierVirtualKey, 0, KeyUpEvent, UIntPtr.Zero);
                keybd_event((byte)firstModifierVirtualKey, 0, KeyUpEvent, UIntPtr.Zero);
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

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
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

internal sealed record PilotSwitchResult(
    int TargetPilotIndex,
    bool Succeeded,
    string? CapturePath);

internal sealed record StartupAutomationSummary(
    string PlayButtonCapturePath,
    bool PlayButtonFound,
    Rect? PlayButtonBounds,
    string? PilotCapturePath = null,
    bool PilotLocated = false,
    Rect? PilotBounds = null,
    bool ShouldStartAutomation = false);

internal sealed record AutomationSummary(
    ScreenCaptureAnalysisSummary CaptureSummary,
    int ClickedPointCount,
    Rect? ControlButtonBounds,
    string FocusedCapturePath,
    bool MaximumSubmissionsReached = false,
    int CurrentPilotIndex = 1,
    int? TargetPilotIndex = null,
    bool PilotSwitchSucceeded = false,
    string? PilotSwitchCapturePath = null);
