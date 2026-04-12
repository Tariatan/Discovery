using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Discovery.Tests;

public sealed class SampleImageProcessorTests
{
    [Fact]
    public void ProcessSamples_ThrowsWhenSamplesDirectoryIsMissing()
    {
        using var workspace = new TemporaryDirectory();
        var processor = new SampleImageProcessor();

        var exception = Assert.Throws<DirectoryNotFoundException>(() => processor.ProcessSamples(workspace.Path));

        Assert.Contains("Samples folder was not found", exception.Message);
    }

    [Fact]
    public void ProcessSamples_ProcessesSupportedImagesAndSkipsGeneratedFiles()
    {
        using var workspace = new TemporaryDirectory();
        var samplesDirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "samples")).FullName;
        CreateSolidImage(Path.Combine(samplesDirectory, "01.png"), 900, 900);

        var processor = new SampleImageProcessor();

        var summary = processor.ProcessSamples(workspace.Path);

        var result = Assert.Single(summary.Results);
        Assert.Equal("01.png", result.FileName);
        Assert.False(result.PlayfieldFound);
        Assert.Equal(0, result.ClusterCount);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal(Path.Combine(samplesDirectory, "output"), summary.OutputDirectory);
    }

    private static void CreateSolidImage(string path, int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        bitmap.Save(path, ImageFormat.Png);
    }
}
