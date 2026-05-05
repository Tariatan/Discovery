namespace Automaton.Tests;

public sealed class LocationChangeTimerLocatorTests
{
    [Fact]
    public void TryLocate_UndockedCompleteImage_ReturnsLocationChangeTimer()
    {
        // Arrange
        using var image = SyntheticMiningImageFactory.CreateUndockedCompleteImage();
        var locator = new LocationChangeTimerLocator();

        // Act
        var located = locator.TryLocate(image, out var location);

        // Assert
        Assert.True(located);
        Assert.InRange(location.Bounds.X, 120, 126);
        Assert.InRange(location.Bounds.Y, 43, 49);
        Assert.True(location.Score >= 0.90);
    }

    [Fact]
    public void TryLocate_UndockedImageWithoutTimer_ReturnsFalse()
    {
        // Arrange
        using var image = SyntheticMiningImageFactory.CreateUndockedImage();
        var locator = new LocationChangeTimerLocator();

        // Act
        var located = locator.TryLocate(image, out _);

        // Assert
        Assert.False(located);
    }
}
