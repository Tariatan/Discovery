using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Discovery.Tests;

public sealed class SampleImageProcessorTests
{
    [Fact]
    public void ProcessSamples_SamplesDirectoryDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var processor = new SampleImageProcessor();

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);
        DirectoryNotFoundException exception;

        try
        {
            exception = Assert.Throws<DirectoryNotFoundException>(() => processor.ProcessSamples());
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.Contains("Samples folder was not found", exception.Message);
    }

    [Fact]
    public void ProcessSamples_SupportedImageExists_ReturnsAnnotatedResult()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var samplesDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "samples")).FullName;
        CreateSolidImage(Path.Combine(samplesDirectory, "01.png"), 900, 900);
        var processor = new SampleImageProcessor();
        SampleProcessingSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = processor.ProcessSamples();
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        var result = Assert.Single(summary.Results);
        Assert.Equal("01.png", result.FileName);
        Assert.False(result.PlayfieldFound);
        Assert.Equal(0, result.ClusterCount);
        Assert.True(File.Exists(Path.Combine(workspace.Path, result.OutputPath)));
        Assert.Equal(Path.Combine("samples", "output"), summary.OutputDirectory);
    }

    [Fact]
    public void ProcessSamples_RealAndBlankSamplesExist_ReturnsDetectionResultsForEachImage()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var samplesDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "samples")).FullName;
        File.Copy(Path.Combine("samples", "05.png"), Path.Combine(samplesDirectory, "05.png"));
        CreateSolidImage(Path.Combine(samplesDirectory, "99.png"), 900, 900);
        var processor = new SampleImageProcessor();
        SampleProcessingSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = processor.ProcessSamples();
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.Equal(2, summary.Results.Count);

        var foundResult = Assert.Single(summary.Results, result => result.FileName == "05.png");
        Assert.True(foundResult.PlayfieldFound);
        Assert.True(foundResult.ClusterCount > 0);
        Assert.True(File.Exists(Path.Combine(workspace.Path, foundResult.OutputPath)));

        var blankResult = Assert.Single(summary.Results, result => result.FileName == "99.png");
        Assert.False(blankResult.PlayfieldFound);
        Assert.Equal(0, blankResult.ClusterCount);
        Assert.True(File.Exists(Path.Combine(workspace.Path, blankResult.OutputPath)));
    }

    private static void CreateSolidImage(string path, int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.Black);
        bitmap.Save(path, ImageFormat.Png);
    }
}
