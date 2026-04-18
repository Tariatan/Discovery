using OpenCvSharp;

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
        var processor = new SampleImageProcessor();
        SampleProcessingSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            Directory.CreateDirectory("samples");
            CreateSolidImage(Path.Combine("samples", "01.png"), 900, 900);
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
    }

    [Fact]
    public void ProcessSamples_RealAndBlankSamplesExist_ReturnsDetectionResultsForEachImage()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var processor = new SampleImageProcessor();
        SampleProcessingSummary summary;

        Directory.CreateDirectory(Path.Combine(workspace.Path, "samples"));
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(Path.Combine(workspace.Path, "samples", "05.sample.png"));
        CreateSolidImage(Path.Combine(workspace.Path, "samples", "99.png"), 900, 900);

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

        var foundResult = Assert.Single(summary.Results, result => result.FileName == "05.sample.png");
        Assert.True(foundResult.PlayfieldFound);
        Assert.True(foundResult.ClusterCount > 0);
        Assert.True(File.Exists(Path.Combine(workspace.Path, foundResult.OutputPath)));

        var blankResult = Assert.Single(summary.Results, result => result.FileName == "99.png");
        Assert.False(blankResult.PlayfieldFound);
        Assert.Equal(0, blankResult.ClusterCount);
        Assert.True(File.Exists(Path.Combine(workspace.Path, blankResult.OutputPath)));
    }

    [Fact]
    public void ResolvePolygonCollisions_PolygonsOverlap_SeparatesThem()
    {
        // Arrange
        var polygons = new List<OpenCvSharp.Point[]>
        {
            new[]
            {
                new OpenCvSharp.Point(0, 0),
                new OpenCvSharp.Point(10, 0),
                new OpenCvSharp.Point(10, 10),
                new OpenCvSharp.Point(0, 10)
            },
            new[]
            {
                new OpenCvSharp.Point(6, 4),
                new OpenCvSharp.Point(12, 4),
                new OpenCvSharp.Point(12, 12),
                new OpenCvSharp.Point(6, 12)
            }
        };

        // Act
        SampleImageProcessor.ResolvePolygonCollisions(polygons);

        // Assert
        using var firstInput = OpenCvSharp.InputArray.Create(polygons[0]);
        using var secondInput = OpenCvSharp.InputArray.Create(polygons[1]);
        using var overlapPolygon = new OpenCvSharp.Mat();
        var overlapArea = Cv2.IntersectConvexConvex(firstInput, secondInput, overlapPolygon, true);
        Assert.True(overlapArea <= 1.0);
    }

    [Fact]
    public void TryFindVerticalSplitRow_BimodalVerticalDensity_ReturnsSplitBetweenLobes()
    {
        // Arrange
        var candidatePoints = new List<OpenCvSharp.Point>();

        for (var y = 0; y < 70; y++)
        {
            for (var x = 0; x < 12; x++)
            {
                candidatePoints.Add(new OpenCvSharp.Point(x, y));
            }
        }

        for (var y = 70; y < 105; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                candidatePoints.Add(new OpenCvSharp.Point(x, y));
            }
        }

        for (var y = 105; y < 175; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                candidatePoints.Add(new OpenCvSharp.Point(x, y));
            }
        }

        // Act
        var splitRow = SampleImageProcessor.TryFindVerticalSplitRow(candidatePoints, 175);

        // Assert
        Assert.NotNull(splitRow);
        Assert.InRange(splitRow.Value, 75, 100);
    }

    [Fact]
    public void ResolvePolygonCollisions_VerticallyStackedPolygonsOverlap_SeparatesThem()
    {
        // Arrange
        var polygons = new List<OpenCvSharp.Point[]>
        {
            new[]
            {
                new OpenCvSharp.Point(118, 95),
                new OpenCvSharp.Point(170, 78),
                new OpenCvSharp.Point(226, 86),
                new OpenCvSharp.Point(238, 140),
                new OpenCvSharp.Point(229, 184),
                new OpenCvSharp.Point(124, 182)
            },
            new[]
            {
                new OpenCvSharp.Point(109, 173),
                new OpenCvSharp.Point(176, 170),
                new OpenCvSharp.Point(238, 178),
                new OpenCvSharp.Point(249, 236),
                new OpenCvSharp.Point(221, 282),
                new OpenCvSharp.Point(146, 279),
                new OpenCvSharp.Point(103, 220)
            }
        };

        // Act
        SampleImageProcessor.ResolvePolygonCollisions(polygons);

        // Assert
        using var firstInput = OpenCvSharp.InputArray.Create(polygons[0]);
        using var secondInput = OpenCvSharp.InputArray.Create(polygons[1]);
        using var overlapPolygon = new OpenCvSharp.Mat();
        var overlapArea = Cv2.IntersectConvexConvex(firstInput, secondInput, overlapPolygon, true);
        Assert.True(overlapArea <= 1.0);
    }

    [Fact]
    public void EnsureMinimumPointSpacing_PolygonPointsAreTooClose_MovesPointsApart()
    {
        // Arrange
        var polygons = new List<OpenCvSharp.Point[]>
        {
            new[]
            {
                new OpenCvSharp.Point(10, 10),
                new OpenCvSharp.Point(20, 10),
                new OpenCvSharp.Point(20, 20),
                new OpenCvSharp.Point(10, 20)
            },
            new[]
            {
                new OpenCvSharp.Point(23, 11),
                new OpenCvSharp.Point(30, 11),
                new OpenCvSharp.Point(30, 22),
                new OpenCvSharp.Point(23, 22)
            }
        };

        // Act
        SampleImageProcessor.EnsureMinimumPointSpacing(polygons);

        // Assert
        var minimumDistance = double.MaxValue;

        foreach (var firstPoint in polygons[0])
        {
            foreach (var secondPoint in polygons[1])
            {
                var dx = firstPoint.X - secondPoint.X;
                var dy = firstPoint.Y - secondPoint.Y;
                minimumDistance = Math.Min(minimumDistance, Math.Sqrt((dx * dx) + (dy * dy)));
            }
        }

        Assert.True(minimumDistance >= 5.0);
    }

    [Fact]
    public void AnalyzeImageFile_MoreThanEightPolygonsDetected_KeepsOnlyEightLargestPolygons()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var imagePath = Path.Combine(workspace.Path, "many-clusters.png");
        using (var image = new Mat(new OpenCvSharp.Size(1200, 900), MatType.CV_8UC3, Scalar.All(0)))
        {
            using var marker = LoadMarkerImage();
            PasteMarker(image, marker, new OpenCvSharp.Point(100, 150));
            PasteMarker(image, marker, new OpenCvSharp.Point(752, 150));
            PasteMarker(image, marker, new OpenCvSharp.Point(100, 752));
            PasteMarker(image, marker, new OpenCvSharp.Point(752, 752));

            for (var index = 0; index < 9; index++)
            {
                var center = new OpenCvSharp.Point(180 + (index % 3 * 180), 220 + (index / 3 * 180));
                Cv2.Ellipse(image, center, new OpenCvSharp.Size(32, 24), 0, 0, 360, new Scalar(0, 160, 255), -1, LineTypes.AntiAlias);
            }

            Cv2.ImWrite(imagePath, image);
        }

        var processor = new SampleImageProcessor();

        // Act
        var analysis = processor.AnalyzeImageFile(imagePath);

        // Assert
        Assert.Equal(8, analysis.Polygons.Count);
        Assert.Equal(8, analysis.Result.ClusterCount);
    }

    private static void CreateSolidImage(string path, int width, int height)
    {
        using var image = new Mat(new OpenCvSharp.Size(width, height), MatType.CV_8UC3, Scalar.All(0));
        Cv2.ImWrite(path, image);
    }

    private static Mat LoadMarkerImage()
    {
        using var bitmap = Discovery.Properties.Resources.marker;
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
        return Cv2.ImDecode(memoryStream.ToArray(), ImreadModes.Color);
    }

    private static void PasteMarker(Mat image, Mat marker, OpenCvSharp.Point location)
    {
        using var roi = new Mat(image, new Rect(location.X, location.Y, marker.Width, marker.Height));
        marker.CopyTo(roi);
    }
}
