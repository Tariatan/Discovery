using OpenCvSharp;

namespace Discovery.Tests;

public sealed class AutomationServiceTests
{
    [Fact]
    public void AutomateCurrentScreen_PlayfieldAndControlButtonExist_ClicksPolygonPointsAndFocusesControlButton()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var fixtureCapturePath = Path.Combine("captures", "capture-20260413-215725.png");
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        File.Copy(fixtureCapturePath, capturePath);
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
            summary = automationService.AutomateCurrentScreen(dpi);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(summary.CaptureSummary.Analysis.Result.PlayfieldFound);
        Assert.True(summary.ClickedPointCount > 0);
        Assert.NotNull(summary.ControlButtonBounds);
        Assert.Equal(summary.ClickedPointCount + 1, automationInputController.MoveTargets.Count); // + Control button focus
        Assert.Equal(summary.ClickedPointCount, automationInputController.ClickCount);
        var finalMoveTarget = automationInputController.MoveTargets[^1];
        Assert.InRange(finalMoveTarget.X, 930, 1200);
        Assert.InRange(finalMoveTarget.Y, 645, 655);
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

        public void LeftClick()
        {
            ClickCount++;
        }

        public void Delay(int milliseconds)
        {
        }
    }
}
