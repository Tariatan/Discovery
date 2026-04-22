using OpenCvSharp;

namespace Discovery.Tests;

public sealed class AutomationServiceTests
{
    [Fact]
    public void AutomateCurrentScreen_PlayfieldAndControlButtonExist_ClicksPolygonPointsAndFocusesControlButton()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(capturePath);
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath => File.Copy(capturePath, outputPath)),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        using var cancellationTokenSource = new CancellationTokenSource();
        var sawAfterSubmitDelay = false;
        var shortDelaysAfterSubmit = 0;
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds),
            OnDelay = milliseconds =>
            {
                if (milliseconds == 5_000)
                {
                    sawAfterSubmitDelay = true;
                    return;
                }

                if (sawAfterSubmitDelay && milliseconds == 300)
                {
                    shortDelaysAfterSubmit++;
                    if (shortDelaysAfterSubmit >= 2)
                    {
                        cancellationTokenSource.Cancel();
                    }
                }
            }
        };
        var automationService = new AutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);
        AutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(summary.CaptureSummary.Analysis.Result.PlayfieldFound);
        Assert.True(summary.ClickedPointCount > 0);
        Assert.NotNull(summary.ControlButtonBounds);
        Assert.Equal("captures", summary.CaptureSummary.CapturesDirectory);
        Assert.Equal(summary.ClickedPointCount + 1, automationInputController.MoveTargets.Count); // + Control button focus
        Assert.Equal(summary.ClickedPointCount + 3, automationInputController.ClickCount);
        var finalMoveTarget = automationInputController.MoveTargets[^1];
        Assert.InRange(finalMoveTarget.X, 930, 1200);
        Assert.InRange(finalMoveTarget.Y, 645, 655);
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.CaptureSummary.CapturePath)));
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.CaptureSummary.Analysis.Result.OutputPath)));
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.FocusedCapturePath)));
        Assert.Equal(
            Path.Combine(
                summary.CaptureSummary.CapturesDirectory,
                $"{Path.GetFileNameWithoutExtension(summary.CaptureSummary.CapturePath)}.focused.png"),
            summary.FocusedCapturePath);
    }

    [Fact]
    public void AutomateCurrentScreen_StopRequestedAfterFirstCycle_StartsNextCycleOnlyWhenNotCanceled()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(capturePath);
        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                File.Copy(capturePath, outputPath);
            }),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        using var cancellationTokenSource = new CancellationTokenSource();
        var sawAfterSubmitDelay = false;
        var shortDelaysAfterSubmit = 0;
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds),
            OnDelay = milliseconds =>
            {
                if (milliseconds == 5_000)
                {
                    sawAfterSubmitDelay = true;
                    return;
                }

                if (sawAfterSubmitDelay && milliseconds == 300)
                {
                    shortDelaysAfterSubmit++;
                    if (shortDelaysAfterSubmit >= 2)
                    {
                        cancellationTokenSource.Cancel();
                    }
                }
            }
        };
        var automationService = new AutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.Equal(2, captureInvocationCount);
    }

    [Fact]
    public void ScalePointForDpi_PointUsesScaledDisplayCoordinates_ReturnsDevicePixelPoint()
    {
        // Arrange
        var point = new Point(1065, 650);
        var dpi = new System.Windows.DpiScale(1.25, 1.25);

        // Act
        var scaledPoint = AutomationService.ScalePointForDpi(point, dpi);

        // Assert
        Assert.Equal(1331, scaledPoint.X);
        Assert.Equal(813, scaledPoint.Y);
    }

    [Fact]
    public void AutomateCurrentScreen_CancellationRequested_StopsBeforeAnyClicks()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(_ => throw new InvalidOperationException("Capture should not run when automation is already canceled.")),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds)
        };
        var automationService = new AutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            Assert.Throws<OperationCanceledException>(() => automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token));
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.Empty(automationInputController.MoveTargets);
        Assert.Equal(0, automationInputController.ClickCount);
    }

    [Fact]
    public void AutomateCurrentScreen_FifthCycleCompletesTooQuickly_WaitsBeforeSubmit()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        CreateSolidImage(capturePath, 900, 900);
        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                File.Copy(capturePath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        using var cancellationTokenSource = new CancellationTokenSource();
        var afterSubmitDelayCount = 0;
        var observedLongRateLimitDelay = false;
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds),
            OnDelay = milliseconds =>
            {
                if (milliseconds > 5_000)
                {
                    observedLongRateLimitDelay = true;
                }

                if (milliseconds == 5_000)
                {
                    afterSubmitDelayCount++;
                    if (afterSubmitDelayCount >= 5)
                    {
                        cancellationTokenSource.Cancel();
                    }
                }
            }
        };
        var automationService = new AutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.Equal(9, captureInvocationCount);
        Assert.True(observedLongRateLimitDelay);
    }

    private sealed class StubScreenCaptureProvider(Action<string> captureAction)
        : ScreenCaptureService.IScreenCaptureProvider
    {
        public void CaptureToFile(string outputPath)
        {
            captureAction(outputPath);
        }
    }

    private sealed class StubAutomationInputController : AutomationService.IAutomationInputController
    {
        public List<Point> MoveTargets { get; } = [];

        public int ClickCount { get; private set; }

        public Action<int>? OnDelay { get; init; }

        public Action<int>? OnDelayAdvanceClock { get; init; }

        public void MoveTo(Point point)
        {
            MoveTargets.Add(point);
        }

        public void LeftClick(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClickCount++;
        }

        public void Delay(int milliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnDelayAdvanceClock?.Invoke(milliseconds);
            OnDelay?.Invoke(milliseconds);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class StubAutomationClock : AutomationService.IAutomationClock
    {
        private DateTime m_UtcNow = new(2026, 4, 22, 12, 0, 0, DateTimeKind.Utc);

        public DateTime UtcNow => m_UtcNow;

        public void AdvanceBy(int milliseconds)
        {
            m_UtcNow = m_UtcNow.AddMilliseconds(milliseconds);
        }
    }

    private static void CreateSolidImage(string path, int width, int height)
    {
        using var image = new Mat(new OpenCvSharp.Size(width, height), MatType.CV_8UC3, Scalar.All(0));
        Cv2.ImWrite(path, image);
    }
}
