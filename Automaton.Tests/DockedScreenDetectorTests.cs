using OpenCvSharp;

namespace Automaton.Tests;

public sealed class DockedScreenDetectorTests
{
    [Fact]
    public void Analyze_DockedItemHangarFocused_ReturnsDockedWithMiningHoldUnfocused()
    {
        // Arrange
        using var image = SyntheticMiningImageFactory.CreateDockedItemHangarFocusedImage();
        var detector = new DockedScreenDetector();

        // Act
        var analysis = detector.Analyze(image);

        // Assert
        Assert.True(analysis.IsDocked);
        Assert.NotNull(analysis.UndockButtonBounds);
        Assert.False(analysis.MiningHoldFocused);
        Assert.True(analysis.ItemHangarFocused);
        Assert.Equal(MiningHoldContentState.Unknown, analysis.MiningHoldContent);
    }

    [Fact]
    public void Analyze_DockedMiningHoldFocusedEmpty_ReturnsEmptyMiningHold()
    {
        // Arrange
        using var image = SyntheticMiningImageFactory.CreateDockedMiningHoldFocusedEmptyImage();
        var detector = new DockedScreenDetector();

        // Act
        var analysis = detector.Analyze(image);

        // Assert
        Assert.True(analysis.IsDocked);
        Assert.True(analysis.MiningHoldFocused);
        Assert.False(analysis.ItemHangarFocused);
        Assert.Equal(MiningHoldContentState.Empty, analysis.MiningHoldContent);
    }

    [Fact]
    public void Analyze_DockedMiningHoldFocusedNotEmpty_ReturnsMiningHoldContainsOre()
    {
        // Arrange
        using var image = SyntheticMiningImageFactory.CreateDockedMiningHoldFocusedNotEmptyImage();
        var detector = new DockedScreenDetector();

        // Act
        var analysis = detector.Analyze(image);

        // Assert
        Assert.True(analysis.IsDocked);
        Assert.True(analysis.MiningHoldFocused);
        Assert.Equal(MiningHoldContentState.ContainsOre, analysis.MiningHoldContent);
    }

    [Fact]
    public void Analyze_UndockedScreen_ReturnsNotDocked()
    {
        // Arrange
        using var image = SyntheticMiningImageFactory.CreateUndockedImage();
        var detector = new DockedScreenDetector();

        // Act
        var analysis = detector.Analyze(image);

        // Assert
        Assert.False(analysis.IsDocked);
        Assert.Null(analysis.UndockButtonBounds);
        Assert.Equal(MiningHoldContentState.Unknown, analysis.MiningHoldContent);
    }
}
