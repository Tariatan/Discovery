using OpenCvSharp;

namespace Discovery.Tests;

public sealed class MaximumSubmissionsPopupDetectorTests
{
    [Fact]
    public void Detect_ImageContainsMaximumSubmissionsPopup_ReturnsTrue()
    {
        // Arrange
        using var image = SyntheticDiscoveryImageFactory.CreateMaximumSubmissionsPopupImage();
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.True(detected);
    }

    [Fact]
    public void Detect_ImageDoesNotContainMaximumSubmissionsPopup_ReturnsFalse()
    {
        // Arrange
        using var image = SyntheticDiscoveryImageFactory.CreateTwoClusterImage();
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.False(detected);
    }
}
