using OpenCvSharp;
using System.IO;
using Serilog;

namespace Automaton;

internal sealed class ProjectDiscoveryAutomationService
{
    private const int StartupDelayMilliseconds = 3_000;
    private const int LauncherStartupDelayMilliseconds = 20_000;
    private const int MaximumSubmissionsPerWindow = 5;
    private const int SubmissionWindowMilliseconds = 70_000;
    private const int MaximumConsecutivePlayfieldMisses = 5;
    private const int InitialPilotIndex = 1;
    private const int MinimumClickDelayMilliseconds = 300;
    private const int MaximumClickDelayMilliseconds = 800;
    private const int AfterSubmitDelayMilliseconds = 4_000;
    private const int HoverDelayMilliseconds = 200;
    private const int WindowActivationDelayMilliseconds = 1_000;
    private const int PilotSelectionConfirmDelayMilliseconds = 1_000;
    private const int PilotLogoutDelayMilliseconds = 30_000;
    private const int PilotLoginDelayMilliseconds = 40_000;
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
    private static readonly ILogger Logger = Log.ForContext<ProjectDiscoveryAutomationService>();

    private readonly ScreenCaptureService m_ScreenCaptureService;
    private readonly IAutomationInputController m_AutomationInputController;
    private readonly IAutomationClock m_AutomationClock;
    private readonly ErrorPopupDetector m_ErrorPopupDetector;
    private readonly PilotAvatarLocator m_PilotAvatarLocator = new();
    private readonly PlayNowButtonLocator m_PlayNowButtonLocator = new();
    private readonly AutomationSubmitRateLimiter m_SubmitRateLimiter = new();
    private readonly Random m_Random = new();
    private int m_CurrentPilotIndex = InitialPilotIndex;

    internal bool KeepDebugImages { get; set; } = true;

    public ProjectDiscoveryAutomationService()
        : this(new ScreenCaptureService(), new AutomationInputController(), new SystemAutomationClock())
    {
    }

    internal ProjectDiscoveryAutomationService(ScreenCaptureService screenCaptureService, IAutomationInputController automationInputController)
        : this(screenCaptureService, automationInputController, new SystemAutomationClock())
    {
    }

    internal ProjectDiscoveryAutomationService(
        ScreenCaptureService screenCaptureService,
        IAutomationInputController automationInputController,
        IAutomationClock automationClock)
    {
        m_ScreenCaptureService = screenCaptureService;
        m_AutomationInputController = automationInputController;
        m_AutomationClock = automationClock;
        m_ErrorPopupDetector = new ErrorPopupDetector();
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
        using var traceImages = CreateTraceImageScope();
        Logger.Information("Preparing launcher startup automation. InitialPilotIndex={InitialPilotIndex}", initialPilotIndex);
        var playButtonCapturePath = m_ScreenCaptureService.CaptureCurrentScreenTrace(".play");
        traceImages.Track(playButtonCapturePath);
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

        Logger.Information("Play button found during startup automation. CapturePath={CapturePath}, Bounds={Bounds}", playButtonCapturePath, playButtonLocation.Bounds);
        m_AutomationInputController.MoveTo(Center(playButtonLocation.Bounds));
        m_AutomationInputController.LeftClick(cancellationToken);
        m_AutomationInputController.Delay(LauncherStartupDelayMilliseconds, cancellationToken);
        m_AutomationInputController.PressKeyChord(VirtualKeyControl, VirtualKeyW, cancellationToken);

        var pilotSelectionCapturePath = m_ScreenCaptureService.CaptureCurrentScreenTrace($".startup-pilot-{initialPilotIndex}");
        traceImages.Track(pilotSelectionCapturePath);
        cancellationToken.ThrowIfCancellationRequested();
        using var pilotSelectionScreen = Cv2.ImRead(pilotSelectionCapturePath);
        if (!m_PilotAvatarLocator.TryLocate(pilotSelectionScreen, initialPilotIndex, out var pilotLocation))
        {
            DrawPilotNotFoundDebugOverlay(pilotSelectionCapturePath, initialPilotIndex);
            Logger.Warning("Pilot was not found during startup automation. PilotIndex={PilotIndex}, CapturePath={CapturePath}", initialPilotIndex, pilotSelectionCapturePath);
            return new StartupAutomationSummary(
                playButtonCapturePath,
                true,
                playButtonLocation.Bounds,
                pilotSelectionCapturePath);
        }

        Logger.Information("Pilot found during startup automation. PilotIndex={PilotIndex}, CapturePath={CapturePath}, Bounds={Bounds}", initialPilotIndex, pilotSelectionCapturePath, pilotLocation.Bounds);
        m_AutomationInputController.MoveTo(Center(pilotLocation.Bounds));
        m_AutomationInputController.LeftClick(cancellationToken);
        m_AutomationInputController.Delay(LauncherStartupDelayMilliseconds, cancellationToken);
        m_CurrentPilotIndex = initialPilotIndex;
        m_AutomationInputController.PressKeyChord(VirtualKeyAlt, VirtualKeyL, cancellationToken);

        return new StartupAutomationSummary(playButtonCapturePath, true, playButtonLocation.Bounds, pilotSelectionCapturePath, true, pilotLocation.Bounds, true);
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
        var consecutivePlayfieldMisses = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var traceImages = CreateTraceImageScope();
                var captureSummary = m_ScreenCaptureService.CaptureAndAnalyzeCurrentScreen();
                traceImages.Track(captureSummary);
                cancellationToken.ThrowIfCancellationRequested();

                if (!captureSummary.Analysis.Result.PlayfieldFound &&
                    m_ErrorPopupDetector.DetectSlowDownAndDrawDebugOverlay(captureSummary.CapturePath))
                {
                    RecoverFromSlowDownPopup(captureSummary.CapturePath, cancellationToken);
                    lastSummary = new AutomationSummary(
                        captureSummary,
                        0,
                        null,
                        captureSummary.CapturePath,
                        CurrentPilotIndex: m_CurrentPilotIndex,
                        SlowDownPopupDetected: true);
                    consecutivePlayfieldMisses = 0;
                    m_AutomationInputController.Delay(MinimumClickDelayMilliseconds, cancellationToken);
                    continue;
                }

                if (captureSummary.Analysis.Result.PlayfieldFound)
                {
                    consecutivePlayfieldMisses = 0;
                }
                else
                {
                    consecutivePlayfieldMisses++;
                    Logger.Warning(
                        "Playfield was not found during automation. CapturePath={CapturePath}, ConsecutivePlayfieldMisses={ConsecutivePlayfieldMisses}, MaximumConsecutivePlayfieldMisses={MaximumConsecutivePlayfieldMisses}",
                        captureSummary.CapturePath,
                        consecutivePlayfieldMisses,
                        MaximumConsecutivePlayfieldMisses);

                    if (consecutivePlayfieldMisses >= MaximumConsecutivePlayfieldMisses)
                    {
                        lastSummary = new AutomationSummary(
                            captureSummary,
                            0,
                            null,
                            string.Empty,
                            CurrentPilotIndex: m_CurrentPilotIndex,
                            PlayfieldMissingLimitReached: true);
                        Logger.Error(
                            "Automation loop stopped because the playfield was not found repeatedly. CapturePath={CapturePath}, ConsecutivePlayfieldMisses={ConsecutivePlayfieldMisses}",
                            captureSummary.CapturePath,
                            consecutivePlayfieldMisses);
                        return lastSummary;
                    }
                }

                lastSummary = AutomateSingleCycle(dpi, m_SubmitRateLimiter, captureSummary, traceImages, cancellationToken);
                if (lastSummary is { MaximumSubmissionsReached: true, PilotSwitchSucceeded: false })
                {
                    Logger.Warning("Automation loop stopped because maximum submissions were reached and pilot switching did not succeed. CurrentPilotIndex={CurrentPilotIndex}, TargetPilotIndex={TargetPilotIndex}", lastSummary.CurrentPilotIndex, lastSummary.TargetPilotIndex);
                    return lastSummary;
                }

                m_AutomationInputController.Delay(MinimumClickDelayMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (lastSummary is not null)
        {
            Logger.Information("Automation loop canceled after a completed cycle. CapturePath={CapturePath}, CurrentPilotIndex={CurrentPilotIndex}", lastSummary.CaptureSummary.CapturePath, lastSummary.CurrentPilotIndex);
            return lastSummary;
        }

        return lastSummary ?? throw new OperationCanceledException(cancellationToken);
    }

    private AutomationSummary AutomateSingleCycle(
        System.Windows.DpiScale dpi,
        AutomationSubmitRateLimiter rateLimiter,
        ScreenCaptureAnalysisSummary captureSummary,
        TraceImageScope traceImages,
        CancellationToken cancellationToken)
    {
        var clickedPointCount = ClickPolygonPoints(captureSummary.Analysis.Polygons, cancellationToken);
        Logger.Information("Automation cycle analyzed screen. CapturePath={CapturePath}, PlayfieldFound={PlayfieldFound}, ClusterCount={ClusterCount}, ClickedPointCount={ClickedPointCount}", captureSummary.CapturePath, captureSummary.Analysis.Result.PlayfieldFound, captureSummary.Analysis.Result.ClusterCount, clickedPointCount);

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
        traceImages.Track(focusedCapturePath);
        var focusedCaptureAnalysis = m_ScreenCaptureService.AnalyzeImageFile(focusedCapturePath, writeAnnotatedOutput: false);

        if (!focusedCaptureAnalysis.Result.PlayfieldFound && m_ErrorPopupDetector.DetectSlowDownAndDrawDebugOverlay(focusedCapturePath))
        {
            RecoverFromSlowDownPopup(focusedCapturePath, cancellationToken);
            return new AutomationSummary(
                captureSummary,
                clickedPointCount,
                ControlButtonBounds,
                focusedCapturePath,
                CurrentPilotIndex: m_CurrentPilotIndex,
                SlowDownPopupDetected: true);
        }

        if (!focusedCaptureAnalysis.Result.PlayfieldFound && m_ErrorPopupDetector.DetectAndDrawDebugOverlay(focusedCapturePath))
        {
            var pilotSwitchResult = SwitchToNextPilot(captureSummary, traceImages, cancellationToken);
            Logger.Warning("Maximum submissions popup detected. FocusedCapturePath={FocusedCapturePath}, CurrentPilotIndex={CurrentPilotIndex}, TargetPilotIndex={TargetPilotIndex}, PilotSwitchSucceeded={PilotSwitchSucceeded}, PilotSwitchCapturePath={PilotSwitchCapturePath}", focusedCapturePath, m_CurrentPilotIndex, pilotSwitchResult.TargetPilotIndex, pilotSwitchResult.Succeeded, pilotSwitchResult.CapturePath);
            return new AutomationSummary(captureSummary, clickedPointCount, ControlButtonBounds, focusedCapturePath, MaximumSubmissionsReached: true, CurrentPilotIndex: m_CurrentPilotIndex, TargetPilotIndex: pilotSwitchResult.TargetPilotIndex, PilotSwitchSucceeded: pilotSwitchResult.Succeeded, PilotSwitchCapturePath: pilotSwitchResult.CapturePath);
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
        TraceImageScope traceImages,
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

        Logger.Information("Switching pilot. CurrentPilotIndex={CurrentPilotIndex}, TargetPilotIndex={TargetPilotIndex}", m_CurrentPilotIndex, nextPilotIndex);
        // Activate pilot logout window
        m_AutomationInputController.Delay(WindowActivationDelayMilliseconds, cancellationToken);
        m_AutomationInputController.PressKeyChord(VirtualKeyAlt, VirtualKeyQ, cancellationToken);
        // Confirm pilot logout window
        m_AutomationInputController.Delay(PilotSelectionConfirmDelayMilliseconds, cancellationToken);
        m_AutomationInputController.PressKey(VirtualKeyEnter, cancellationToken);

        // Wait for full logout
        m_AutomationInputController.Delay(PilotLogoutDelayMilliseconds, cancellationToken);

        // Close any windows on login screen
        m_AutomationInputController.Delay(MinimumClickDelayMilliseconds, cancellationToken);
        m_AutomationInputController.PressKeyChord(VirtualKeyControl, VirtualKeyW, cancellationToken);

        // Make screenshot of pilots on login screen
        var pilotSelectionCapturePath = CapturePilotSelectionScreenTrace(captureSummary, nextPilotIndex, cancellationToken);
        traceImages.Track(pilotSelectionCapturePath);
        using var pilotSelectionScreen = Cv2.ImRead(pilotSelectionCapturePath);

        // Locate next pilot
        if (!m_PilotAvatarLocator.TryLocate(pilotSelectionScreen, nextPilotIndex, out var location))
        {
            // Failed to locate requested pilot
            DrawPilotNotFoundDebugOverlay(pilotSelectionCapturePath, nextPilotIndex);
            
            Logger.Warning("Target pilot was not found. TargetPilotIndex={TargetPilotIndex}, CapturePath={CapturePath}", nextPilotIndex, pilotSelectionCapturePath);
            return new PilotSwitchResult(nextPilotIndex, Succeeded: false, pilotSelectionCapturePath);
        }

        // Login requested pilot
        m_AutomationInputController.MoveTo(Center(location.Bounds));
        m_AutomationInputController.LeftClick(cancellationToken);
        m_AutomationInputController.Delay(PilotLoginDelayMilliseconds, cancellationToken);
        m_CurrentPilotIndex = nextPilotIndex;

        // Close any window after login
        m_AutomationInputController.PressKeyChord(VirtualKeyControl, VirtualKeyW, cancellationToken);

        // Activate Project Discovery window
        m_AutomationInputController.Delay(MinimumClickDelayMilliseconds, cancellationToken);
        m_AutomationInputController.PressKeyChord(VirtualKeyAlt, VirtualKeyL, cancellationToken);

        Logger.Information("Pilot switch succeeded. CurrentPilotIndex={CurrentPilotIndex}, CapturePath={CapturePath}, Bounds={Bounds}", m_CurrentPilotIndex, pilotSelectionCapturePath, location.Bounds);
        return new PilotSwitchResult(nextPilotIndex, Succeeded: true, pilotSelectionCapturePath);
    }

    private void RecoverFromSlowDownPopup(string focusedCapturePath, CancellationToken cancellationToken)
    {
        Logger.Warning(
            "Slow Down popup detected. FocusedCapturePath={FocusedCapturePath}, RecoveryDelayMilliseconds={RecoveryDelayMilliseconds}",
            focusedCapturePath,
            SubmissionWindowMilliseconds);
        m_AutomationInputController.PressKeyChord(VirtualKeyControl, VirtualKeyW, cancellationToken);
        m_AutomationInputController.Delay(SubmissionWindowMilliseconds, cancellationToken);
        m_AutomationInputController.PressKeyChord(VirtualKeyAlt, VirtualKeyL, cancellationToken);
    }

    private TraceImageScope CreateTraceImageScope()
    {
        return new TraceImageScope(KeepDebugImages);
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

    private sealed class TraceImageScope(bool keepImages) : IDisposable
    {
        private readonly HashSet<string> m_ImagePaths = new(StringComparer.OrdinalIgnoreCase);

        public void Track(ScreenCaptureAnalysisSummary captureSummary)
        {
            Track(captureSummary.CapturePath);
            Track(captureSummary.Analysis.Result.OutputPath);
        }

        public void Track(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return;
            }

            m_ImagePaths.Add(Path.GetFullPath(imagePath));
        }

        public void Dispose()
        {
            if (keepImages)
            {
                return;
            }

            foreach (var imagePath in m_ImagePaths)
            {
                DeleteImageFile(imagePath);
            }
        }

        private static void DeleteImageFile(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    return;
                }

                File.Delete(imagePath);
                Logger.Debug("Deleted trace image. ImagePath={ImagePath}", imagePath);
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Could not delete trace image. ImagePath={ImagePath}", imagePath);
            }
        }
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
    string? PilotSwitchCapturePath = null,
    bool PlayfieldMissingLimitReached = false,
    bool SlowDownPopupDetected = false);
