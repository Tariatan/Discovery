using OpenCvSharp;

namespace Discovery.Tests;

public sealed class PlayfieldDetectionResultTests
{
    [Fact]
    public void NotFound_HasNoBoundsAndReportsFalse()
    {
        Assert.False(PlayfieldDetectionResult.NotFound.IsFound);
        Assert.Empty(PlayfieldDetectionResult.NotFound.MarkerBounds);
    }

    [Fact]
    public void IsFound_ReturnsTrueWhenBoundsAreNonEmpty()
    {
        var result = new PlayfieldDetectionResult(new Rect(10, 20, 30, 40), Array.Empty<Rect>());

        Assert.True(result.IsFound);
    }
}
