using System.Drawing;
using System.Drawing.Imaging;

namespace Discovery.Tests;

public sealed class ScreenCaptureServiceTests
{
    [Fact]
    public void CaptureAndProcessCurrentScreen_CaptureProviderCreatesImage_ReturnsProcessedSummary()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var fixtureSamplePath = Path.Combine("samples", "05.png");
        var samplePath = Path.Combine(workspace.Path, "fixture-05.png");
        File.Copy(fixtureSamplePath, samplePath);
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
        Assert.Equal(Path.Combine("captures", "output"), summary.OutputDirectory);
        Assert.True(summary.Result.PlayfieldFound);
        Assert.True(summary.Result.ClusterCount > 0);
    }

    [Fact]
    public void CaptureAndProcessCurrentScreen_CaptureProviderCreatesBlankImage_ReturnsNotFoundSummary()
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
        Assert.Equal(0, summary.Result.ClusterCount);
    }

    private static void CreateBlankCapture(string outputPath)
    {
        using var bitmap = new Bitmap(1200, 900);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        bitmap.Save(outputPath, ImageFormat.Png);
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
