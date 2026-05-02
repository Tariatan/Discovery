using OpenCvSharp;

namespace Automaton.Tests;

public sealed class PlayfieldDetectorIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Detect_SyntheticImageContainsPlayfield_ReturnsDetectedPlayfield(bool includeSecondCluster)
    {
        // Arrange
        using var image = includeSecondCluster
            ? SyntheticDiscoveryImageFactory.CreateTwoClusterImage()
            : SyntheticDiscoveryImageFactory.CreateSingleClusterImage();
        var detector = new PlayfieldDetector();

        // Act
        var result = detector.Detect(image);

        // Assert
        Assert.True(result.IsFound);
        Assert.Equal(4, result.MarkerBounds.Count);
        Assert.InRange(result.Bounds.Width, 600, 800);
        Assert.InRange(result.Bounds.Height, 600, 800);
    }

    [Fact]
    public void Detect_ImageDoesNotContainPlayfield_ReturnsNotFound()
    {
        // Arrange
        using var image = new Mat(new Size(1200, 900), MatType.CV_8UC3, Scalar.All(0));
        var detector = new PlayfieldDetector();

        // Act
        var result = detector.Detect(image);

        // Assert
        Assert.False(result.IsFound);
        Assert.Empty(result.MarkerBounds);
    }
}
