using OpenCvSharp;

namespace Automaton.Tests;

public sealed class ScreenCaptureServiceTests
{
    [Fact]
    public void CaptureAndProcessCurrentScreen_CaptureProviderCreatesImage_ReturnsProcessedSummary()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var samplePath = Path.Combine(workspace.Path, "fixture-05.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(samplePath);
        var screenCaptureProvider = new StubScreenCaptureProvider(outputPath => File.Copy(samplePath, outputPath));
        var screenCaptureService = new ScreenCaptureService(screenCaptureProvider, new SampleImageProcessor());
        ScreenCaptureSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = screenCaptureService.CaptureAndProcessCurrentScreen();
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.CapturePath)));
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.Result.OutputPath)));
        Assert.Equal("captures", summary.CapturesDirectory);
        Assert.Equal(Path.Combine("captures", Path.GetFileNameWithoutExtension(summary.CapturePath) + ".annotated.png"), summary.Result.OutputPath);
        Assert.True(summary.Result.PlayfieldFound);
        Assert.True(summary.Result.ClusterCount > 0);
    }

    [Fact]
    public void CaptureAndProcessCurrentScreen_CaptureProviderCreatesBlankImage_ReturnsFallbackSummary()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var screenCaptureProvider = new StubScreenCaptureProvider(CreateBlankCapture);
        var screenCaptureService = new ScreenCaptureService(screenCaptureProvider, new SampleImageProcessor());
        ScreenCaptureSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = screenCaptureService.CaptureAndProcessCurrentScreen();
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.CapturePath)));
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.Result.OutputPath)));
        Assert.False(summary.Result.PlayfieldFound);
        Assert.Equal(2, summary.Result.ClusterCount);
    }

    private static void CreateBlankCapture(string outputPath)
    {
        using var image = new Mat(new Size(1200, 900), MatType.CV_8UC3, Scalar.All(0));
        Cv2.ImWrite(outputPath, image);
    }

    private sealed class StubScreenCaptureProvider : ScreenCaptureService.IScreenCaptureProvider
    {
        private readonly Action<string> m_CaptureAction;

        public StubScreenCaptureProvider(Action<string> captureAction)
        {
            m_CaptureAction = captureAction;
        }

        public void CaptureToFile(string outputPath)
        {
            m_CaptureAction(outputPath);
        }
    }
}
