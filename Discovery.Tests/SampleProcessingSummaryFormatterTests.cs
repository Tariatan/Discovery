namespace Discovery.Tests;

public sealed class SampleProcessingSummaryFormatterTests
{
    [Fact]
    public void BuildSummaryText_SummaryContainsResults_FormatsOutputLines()
    {
        // Arrange
        var summary = new SampleProcessingSummary(
            @"samples",
            @"samples\output",
            [
                new SampleProcessingResult("01.png", true, 2, @"samples\output\01.annotated.png"),
                new SampleProcessingResult("02.png", false, 0, @"samples\output\02.annotated.png")
            ]);

        // Act
        var text = SampleProcessingSummaryFormatter.BuildSummaryText(summary);

        // Assert
        var lines = text.Split(Environment.NewLine);
        Assert.Equal("Samples folder: samples", lines[0]);
        Assert.Equal("Debug output:  samples\\output", lines[1]);
        Assert.Equal(string.Empty, lines[2]);
        Assert.Contains("01.png", lines[3]);
        Assert.Contains("playfield=yes", lines[3]);
        Assert.Contains("clusters=2", lines[3]);
        Assert.Contains("02.png", lines[4]);
        Assert.Contains("playfield=no", lines[4]);
        Assert.Contains("clusters=0", lines[4]);
    }
}
