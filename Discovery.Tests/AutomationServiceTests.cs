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
        var automationInputController = new StubAutomationInputController();
        var automationService = new AutomationService(screenCaptureService, automationInputController);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);
        AutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.AutomateCurrentScreen(dpi, CancellationToken.None);
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
        Assert.InRange(automationInputController.ClickCount, summary.ClickedPointCount, summary.ClickedPointCount + 1);
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
        var automationInputController = new StubAutomationInputController();
        var automationService = new AutomationService(screenCaptureService, automationInputController);
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
        }
    }
}
