namespace Automaton.Tests;

public sealed class AsteroidBeltOverviewDetectorTests
{
    [Fact]
    public void Analyze_OverviewWithAsteroidBelts_ReturnsControlsAndBeltRows()
    {
        // Arrange
        using var image = SyntheticMiningImageFactory.CreateWarpToAsteroidFieldImage();
        var detector = new AsteroidBeltOverviewDetector();

        // Act
        var analysis = detector.Analyze(image);

        // Assert
        Assert.True(analysis.OverviewLocated);
        Assert.NotNull(analysis.OverviewBounds);
        Assert.NotNull(analysis.OverviewBeltButtonBounds);
        Assert.Equal(4, analysis.AsteroidBelts.Count);
    }

    [Fact]
    public void Analyze_UndockedImageWithoutOverview_ReturnsNotFound()
    {
        // Arrange
        using var image = SyntheticMiningImageFactory.CreateUndockedCompleteImage();
        var detector = new AsteroidBeltOverviewDetector();

        // Act
        var analysis = detector.Analyze(image);

        // Assert
        Assert.False(analysis.OverviewLocated);
        Assert.Null(analysis.OverviewBeltButtonBounds);
        Assert.Empty(analysis.AsteroidBelts);
    }
}
