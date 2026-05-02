using OpenCvSharp;

namespace Automaton.Tests;

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
        Assert.Equal(2, result.ClusterCount);
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
        Assert.Equal(2, blankResult.ClusterCount);
        Assert.True(File.Exists(Path.Combine(workspace.Path, blankResult.OutputPath)));
    }

    [Fact]
    public void AnalyzeImageFile_PlayfieldNotFound_UsesFallbackPolygons()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var imagePath = Path.Combine(workspace.Path, "blank.png");
        CreateSolidImage(imagePath, 1200, 900);
        var processor = new SampleImageProcessor();
        SampleImageAnalysisResult analysis;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            analysis = processor.AnalyzeImageFile(imagePath);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.False(analysis.PlayfieldDetection.IsFound);
        Assert.Equal(2, analysis.Polygons.Count);
        Assert.Equal(2, analysis.Result.ClusterCount);
        Assert.True(File.Exists(Path.Combine(workspace.Path, analysis.Result.OutputPath)));
    }

    [Fact]
    public void AnalyzeImageFile_FullscreenCapturePlayfieldNotFound_ScalesFallbackPolygonsToDiscoveryPlot()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var imagePath = Path.Combine(workspace.Path, "fullscreen.png");
        CreateSolidImage(imagePath, 1792, 1414);
        var processor = new SampleImageProcessor();

        // Act
        var analysis = processor.AnalyzeImageFile(imagePath);

        // Assert
        Assert.False(analysis.PlayfieldDetection.IsFound);
        Assert.Equal(2, analysis.Polygons.Count);

        var fallbackBounds = Cv2.BoundingRect(analysis.Polygons.SelectMany(polygon => polygon).ToArray());
        Assert.InRange(fallbackBounds.Left, 75, 90);
        Assert.InRange(fallbackBounds.Top, 185, 195);
        Assert.InRange(fallbackBounds.Right, 635, 645);
        Assert.InRange(fallbackBounds.Bottom, 725, 735);
    }

    [Fact]
    public void AnalyzeImageFile_KnownSampleTemplateExists_UsesExpectedPolygons()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "expected"));

        var samplePath = Path.Combine(workspace.Path, "expected", "01.sample.png");
        var expectedPath = Path.Combine(workspace.Path, "expected", "01.sample.expected.png");
        var capturePath = Path.Combine(workspace.Path, "capture.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(samplePath);
        File.Copy(samplePath, capturePath);

        var templatePolygons = new[]
        {
            new[]
            {
                new Point(120, 110),
                new Point(330, 110),
                new Point(330, 260),
                new Point(120, 260)
            },
            new[]
            {
                new Point(150, 360),
                new Point(340, 360),
                new Point(340, 540),
                new Point(150, 540)
            }
        };
        WriteExpectedOverlay(samplePath, expectedPath, templatePolygons);

        var processor = new SampleImageProcessor();

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);
        SampleImageAnalysisResult analysis;

        try
        {
            analysis = processor.AnalyzeImageFile(capturePath);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(analysis.PlayfieldDetection.IsFound);
        Assert.Equal(templatePolygons.Length, analysis.Polygons.Count);
        Assert.EndsWith(".annotated.byexample.01.png", analysis.Result.OutputPath, StringComparison.OrdinalIgnoreCase);

        foreach (var templatePolygon in templatePolygons)
        {
            var expectedCenter = GetPolygonCenter(TranslatePolygon(templatePolygon, analysis.PlayfieldDetection.Bounds.Location));
            Assert.Contains(
                analysis.Polygons,
                polygon => Distance(expectedCenter, GetPolygonCenter(polygon)) < 35.0);
        }
    }

    [Fact]
    public void AnalyzeImageFile_KnownSampleTemplateHasNearbyPolygons_KeepsExpectedRegionsSeparate()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "expected"));

        var samplePath = Path.Combine(workspace.Path, "expected", "02.sample.png");
        var expectedPath = Path.Combine(workspace.Path, "expected", "02.sample.expected.png");
        var capturePath = Path.Combine(workspace.Path, "capture.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(samplePath);
        File.Copy(samplePath, capturePath);

        var templatePolygons = new[]
        {
            new[]
            {
                new Point(115, 100),
                new Point(300, 100),
                new Point(300, 260),
                new Point(115, 260)
            },
            new[]
            {
                new Point(320, 140),
                new Point(430, 140),
                new Point(430, 330),
                new Point(320, 330)
            }
        };
        WriteExpectedOverlay(samplePath, expectedPath, templatePolygons);

        var processor = new SampleImageProcessor();

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);
        SampleImageAnalysisResult analysis;

        try
        {
            analysis = processor.AnalyzeImageFile(capturePath);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(analysis.PlayfieldDetection.IsFound);
        Assert.Equal(templatePolygons.Length, analysis.Polygons.Count);

        foreach (var templatePolygon in templatePolygons)
        {
            var expectedCenter = GetPolygonCenter(TranslatePolygon(templatePolygon, analysis.PlayfieldDetection.Bounds.Location));
            Assert.Contains(
                analysis.Polygons,
                polygon => Distance(expectedCenter, GetPolygonCenter(polygon)) < 40.0);
        }
    }

    [Fact]
    public void AnalyzeImageFile_KnownSampleMaskedTemplateExists_UsesMaskedPolygons()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "expected"));

        var samplePath = Path.Combine(workspace.Path, "expected", "02.sample.png");
        var expectedPath = Path.Combine(workspace.Path, "expected", "02.sample.expected.png");
        var maskedExpectedPath = Path.Combine(workspace.Path, "expected", "02.sample.expected.masked.png");
        var capturePath = Path.Combine(workspace.Path, "capture.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(samplePath);
        File.Copy(samplePath, capturePath);

        var templatePolygons = new[]
        {
            new[]
            {
                new Point(120, 110),
                new Point(330, 110),
                new Point(330, 260),
                new Point(120, 260)
            },
            new[]
            {
                new Point(150, 360),
                new Point(340, 360),
                new Point(340, 540),
                new Point(150, 540)
            }
        };
        WriteExpectedOverlay(samplePath, expectedPath, templatePolygons);
        WriteMaskedExpectedOverlay(samplePath, maskedExpectedPath, templatePolygons);

        var processor = new SampleImageProcessor();

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);
        SampleImageAnalysisResult analysis;

        try
        {
            analysis = processor.AnalyzeImageFile(capturePath);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(analysis.PlayfieldDetection.IsFound);
        Assert.Equal(templatePolygons.Length, analysis.Polygons.Count);

        foreach (var templatePolygon in templatePolygons)
        {
            var expectedCenter = GetPolygonCenter(TranslatePolygon(templatePolygon, analysis.PlayfieldDetection.Bounds.Location));
            Assert.Contains(
                analysis.Polygons,
                polygon => Distance(expectedCenter, GetPolygonCenter(polygon)) < 35.0);
        }
    }

    [Fact]
    public void AnalyzeImageFile_KnownSampleMaskedTemplateContainsTopPolygon_KeepsTopRegion()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "expected"));

        var samplePath = Path.Combine(workspace.Path, "expected", "15.sample.png");
        var expectedPath = Path.Combine(workspace.Path, "expected", "15.sample.expected.png");
        var maskedExpectedPath = Path.Combine(workspace.Path, "expected", "15.sample.expected.masked.png");
        var capturePath = Path.Combine(workspace.Path, "capture.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(samplePath);
        File.Copy(samplePath, capturePath);

        var templatePolygons = new[]
        {
            new[]
            {
                new Point(120, 20),
                new Point(310, 20),
                new Point(310, 150),
                new Point(120, 150)
            },
            new[]
            {
                new Point(150, 360),
                new Point(340, 360),
                new Point(340, 540),
                new Point(150, 540)
            }
        };
        WriteExpectedOverlay(samplePath, expectedPath, templatePolygons);
        WriteMaskedExpectedOverlay(samplePath, maskedExpectedPath, templatePolygons);

        var processor = new SampleImageProcessor();

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);
        SampleImageAnalysisResult analysis;

        try
        {
            analysis = processor.AnalyzeImageFile(capturePath);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(analysis.PlayfieldDetection.IsFound);
        Assert.Equal(templatePolygons.Length, analysis.Polygons.Count);
        Assert.Contains(
            analysis.Polygons,
            polygon => Cv2.BoundingRect(polygon).Top <= analysis.PlayfieldDetection.Bounds.Top + 60);
    }

    [Fact]
    public void RandomizePolygons_PolygonExists_MovesConfiguredPointsWithinDistanceRange()
    {
        // Arrange
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(10, 10),
                new Point(30, 10),
                new Point(40, 25),
                new Point(25, 40),
                new Point(10, 30)
            }
        };
        var originalPolygon = polygons[0].ToArray();

        // Act
        SampleImageProcessor.RandomizePolygons(polygons, new Random(12345));

        // Assert
        var movedDistances = originalPolygon
            .Zip(polygons[0], (originalPoint, randomizedPoint) =>
            {
                var dx = randomizedPoint.X - originalPoint.X;
                var dy = randomizedPoint.Y - originalPoint.Y;
                return Math.Sqrt((dx * dx) + (dy * dy));
            })
            .Where(distance => distance > 0.0)
            .ToArray();

        Assert.Equal(5, movedDistances.Length);
        Assert.All(movedDistances, distance => Assert.InRange(distance, 10.0, 35.5));
    }

    [Fact]
    public void RandomizePolygons_ThenApplyConstraints_PolygonsRemainInsideMarkerFrame()
    {
        // Arrange
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(110, 110),
                new Point(210, 110),
                new Point(210, 210),
                new Point(110, 210),
                new Point(90, 160)
            },
            new[]
            {
                new Point(220, 220),
                new Point(300, 220),
                new Point(300, 320),
                new Point(220, 320),
                new Point(205, 270)
            }
        };
        var markers = new[]
        {
            new Rect(100, 100, 30, 30),
            new Rect(300, 100, 30, 30),
            new Rect(100, 500, 30, 30),
            new Rect(300, 500, 30, 30)
        };

        // Act
        SampleImageProcessor.RandomizePolygons(polygons, new Random(54321));
        var constrainedPolygons = SampleImageProcessor.ApplyMarkerBoundaryConstraints(polygons, markers);

        // Assert
        Assert.All(
            constrainedPolygons,
            polygon =>
            {
                var bounds = Cv2.BoundingRect(polygon);
                Assert.True(bounds.Left >= 100);
                Assert.True(bounds.Top >= 100);
                Assert.True(bounds.Right <= 331);
                Assert.True(bounds.Bottom <= 531);
            });
    }

    [Fact]
    public void NormalizePolygon_PolygonCurvesInward_RemovesInwardVertices()
    {
        // Arrange
        var polygon = new[]
        {
            new Point(10, 10),
            new Point(50, 10),
            new Point(32, 24),
            new Point(50, 50),
            new Point(10, 50)
        };

        // Act
        var normalizedPolygon = SampleImageProcessor.NormalizePolygon(polygon);

        // Assert
        Assert.True(Cv2.IsContourConvex(normalizedPolygon));
        Assert.True(normalizedPolygon.Length < polygon.Length);
        Assert.DoesNotContain(normalizedPolygon, point => point == new Point(32, 24));
    }

    [Fact]
    public void NormalizePolygon_NextPointIsCloseToPreviousPoint_SkipsNextPoint()
    {
        // Arrange
        var polygon = new[]
        {
            new Point(0, 0),
            new Point(20, -20),
            new Point(50, 0),
            new Point(50, 50),
            new Point(0, 50)
        };

        // Act
        var normalizedPolygon = SampleImageProcessor.NormalizePolygon(polygon);

        // Assert
        Assert.Equal(4, normalizedPolygon.Length);
        Assert.DoesNotContain(normalizedPolygon, point => point == new Point(20, -20));
    }

    [Fact]
    public void NormalizePolygon_ThreePointPolygonHasCloseNeighbor_KeepsTriangle()
    {
        // Arrange
        var polygon = new[]
        {
            new Point(0, 0),
            new Point(20, -20),
            new Point(50, 0)
        };

        // Act
        var normalizedPolygon = SampleImageProcessor.NormalizePolygon(polygon);

        // Assert
        Assert.Equal(3, normalizedPolygon.Length);
    }

    [Fact]
    public void NormalizePolygons_AfterSpacingDistortsPolygon_RestoresOutwardOnlyContour()
    {
        // Arrange
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(20, 20),
                new Point(70, 20),
                new Point(70, 70),
                new Point(20, 70)
            },
            new[]
            {
                new Point(78, 24),
                new Point(118, 24),
                new Point(118, 66),
                new Point(78, 66)
            }
        };

        // Act
        SampleImageProcessor.EnsureMinimumPointSpacing(polygons);
        SampleImageProcessor.NormalizePolygons(polygons);

        // Assert
        Assert.All(polygons, polygon => Assert.True(Cv2.IsContourConvex(polygon)));
    }

    [Fact]
    public void ResolvePolygonCollisions_PolygonsOverlap_SeparatesThem()
    {
        // Arrange
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(0, 0),
                new Point(10, 0),
                new Point(10, 10),
                new Point(0, 10)
            },
            new[]
            {
                new Point(6, 4),
                new Point(12, 4),
                new Point(12, 12),
                new Point(6, 12)
            }
        };

        // Act
        SampleImageProcessor.ResolvePolygonCollisions(polygons);

        // Assert
        using var firstInput = InputArray.Create(polygons[0]);
        using var secondInput = InputArray.Create(polygons[1]);
        using var overlapPolygon = new Mat();
        var overlapArea = Cv2.IntersectConvexConvex(firstInput, secondInput, overlapPolygon);
        Assert.True(overlapArea <= 1.0);
    }

    [Fact]
    public void TryFindVerticalSplitRow_BimodalVerticalDensity_ReturnsSplitBetweenLobes()
    {
        // Arrange
        var candidatePoints = new List<Point>();

        for (var y = 0; y < 70; y++)
        {
            for (var x = 0; x < 12; x++)
            {
                candidatePoints.Add(new Point(x, y));
            }
        }

        for (var y = 70; y < 105; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                candidatePoints.Add(new Point(x, y));
            }
        }

        for (var y = 105; y < 175; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                candidatePoints.Add(new Point(x, y));
            }
        }

        // Act
        var splitRow = SampleImageProcessor.TryFindVerticalSplitRow(candidatePoints, 175);

        // Assert
        Assert.NotNull(splitRow);
        Assert.InRange(splitRow.Value, 75, 100);
    }

    [Fact]
    public void TryFindHorizontalSplitColumn_BimodalHorizontalDensity_ReturnsSplitBetweenLobes()
    {
        // Arrange
        var candidatePoints = new List<Point>();

        for (var x = 0; x < 70; x++)
        {
            for (var y = 0; y < 12; y++)
            {
                candidatePoints.Add(new Point(x, y));
            }
        }

        for (var x = 70; x < 105; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                candidatePoints.Add(new Point(x, y));
            }
        }

        for (var x = 105; x < 175; x++)
        {
            for (var y = 0; y < 16; y++)
            {
                candidatePoints.Add(new Point(x, y));
            }
        }

        // Act
        var splitColumn = SampleImageProcessor.TryFindHorizontalSplitColumn(candidatePoints, 175);

        // Assert
        Assert.NotNull(splitColumn);
        Assert.InRange(splitColumn.Value, 75, 100);
    }

    [Fact]
    public void ResolvePolygonCollisions_VerticallyStackedPolygonsOverlap_SeparatesThem()
    {
        // Arrange
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(118, 95),
                new Point(170, 78),
                new Point(226, 86),
                new Point(238, 140),
                new Point(229, 184),
                new Point(124, 182)
            },
            new[]
            {
                new Point(109, 173),
                new Point(176, 170),
                new Point(238, 178),
                new Point(249, 236),
                new Point(221, 282),
                new Point(146, 279),
                new Point(103, 220)
            }
        };

        // Act
        SampleImageProcessor.ResolvePolygonCollisions(polygons);

        // Assert
        using var firstInput = InputArray.Create(polygons[0]);
        using var secondInput = InputArray.Create(polygons[1]);
        using var overlapPolygon = new Mat();
        var overlapArea = Cv2.IntersectConvexConvex(firstInput, secondInput, overlapPolygon);
        Assert.True(overlapArea <= 1.0);
    }

    [Fact]
    public void EnsureMinimumPointSpacing_PolygonPointsAreTooClose_MovesPointsApart()
    {
        // Arrange
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(10, 10),
                new Point(20, 10),
                new Point(20, 20),
                new Point(10, 20)
            },
            new[]
            {
                new Point(23, 11),
                new Point(30, 11),
                new Point(30, 22),
                new Point(23, 22)
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

        Assert.True(minimumDistance >= 15.0);
    }

    [Fact]
    public void EnsureMinimumPointSpacing_PolygonPointIsTooCloseToOtherPolygonEdge_MovesPointAwayFromEdge()
    {
        // Arrange
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(10, 10),
                new Point(40, 10),
                new Point(40, 80),
                new Point(10, 80)
            },
            new[]
            {
                new Point(48, 30),
                new Point(78, 30),
                new Point(78, 60),
                new Point(48, 60)
            }
        };

        // Act
        SampleImageProcessor.EnsureMinimumPointSpacing(polygons);

        // Assert
        var minimumDistance = double.MaxValue;

        foreach (var point in polygons[1])
        {
            for (var edgeIndex = 0; edgeIndex < polygons[0].Length; edgeIndex++)
            {
                var start = polygons[0][edgeIndex];
                var end = polygons[0][(edgeIndex + 1) % polygons[0].Length];
                minimumDistance = Math.Min(
                    minimumDistance,
                    DistancePointToSegment(point, start, end));
            }
        }

        Assert.True(minimumDistance >= 15.0);
    }

    [Fact]
    public void ApplyMarkerBoundaryConstraints_PolygonCrossesMarkerFrame_ClipsPolygonInsideMarkerBounds()
    {
        // Arrange
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(90, 90),
                new Point(340, 90),
                new Point(340, 540),
                new Point(90, 540)
            },
            new[]
            {
                new Point(150, 260),
                new Point(260, 260),
                new Point(260, 360),
                new Point(150, 360)
            }
        };
        var markers = new[]
        {
            new Rect(100, 100, 30, 30),
            new Rect(300, 102, 30, 30),
            new Rect(100, 500, 30, 30),
            new Rect(300, 500, 30, 30)
        };

        // Act
        var adjustedPolygons = SampleImageProcessor.ApplyMarkerBoundaryConstraints(polygons, markers);

        // Assert
        Assert.Equal(2, adjustedPolygons.Count);

        var constrainedBounds = Cv2.BoundingRect(adjustedPolygons[0]);
        Assert.True(constrainedBounds.Left >= 100);
        Assert.True(constrainedBounds.Top >= 100);
        Assert.True(constrainedBounds.Right <= 331);
        Assert.True(constrainedBounds.Bottom <= 531);

        var untouchedBounds = Cv2.BoundingRect(adjustedPolygons[1]);
        Assert.True(untouchedBounds.Left >= 150);
        Assert.True(untouchedBounds.Top >= 260);
        Assert.True(untouchedBounds.Right <= 261);
        Assert.True(untouchedBounds.Bottom <= 361);
    }

    [Fact]
    public void ApplyMarkerBoundaryConstraints_TopBandPolygonCrossesUpperMarkerRow_ClipsBelowTopMarkerTop()
    {
        // Arrange
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(110, 100),
                new Point(260, 100),
                new Point(260, 180),
                new Point(110, 180)
            },
            new[]
            {
                new Point(130, 220),
                new Point(260, 220),
                new Point(260, 360),
                new Point(130, 360)
            }
        };
        var markers = new[]
        {
            new Rect(100, 100, 30, 30),
            new Rect(300, 102, 30, 30),
            new Rect(100, 500, 30, 30),
            new Rect(300, 500, 30, 30)
        };

        // Act
        var adjustedPolygons = SampleImageProcessor.ApplyMarkerBoundaryConstraints(polygons, markers);

        // Assert
        Assert.Equal(2, adjustedPolygons.Count);
        Assert.True(Cv2.BoundingRect(adjustedPolygons[0]).Top >= 100);
        Assert.True(Cv2.BoundingRect(adjustedPolygons[1]).Top >= 220);
    }

    [Fact]
    public void FinalizeDetectedPolygons_FinalConstraintWouldReintroduceCollision_SeparatesPolygonsAgain()
    {
        // Arrange
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(240, 120),
                new Point(311, 112),
                new Point(320, 250),
                new Point(255, 255)
            },
            new[]
            {
                new Point(300, 125),
                new Point(356, 118),
                new Point(352, 248),
                new Point(302, 256)
            }
        };
        var markers = new[]
        {
            new Rect(100, 100, 30, 30),
            new Rect(300, 100, 30, 30),
            new Rect(100, 500, 30, 30),
            new Rect(300, 500, 30, 30)
        };

        // Act
        SampleImageProcessor.FinalizeDetectedPolygons(polygons, markers);

        // Assert
        using var firstInput = InputArray.Create(polygons[0]);
        using var secondInput = InputArray.Create(polygons[1]);
        using var overlapPolygon = new Mat();
        var overlapArea = Cv2.IntersectConvexConvex(firstInput, secondInput, overlapPolygon);
        Assert.True(overlapArea <= 1.0);
    }

    [Fact]
    public void EnforceMinimumPolygonFootprint_PolygonIsTooSmall_ExpandsBoundsToMinimumSize()
    {
        // Arrange
        var polygon = new[]
        {
            new Point(40, 40),
            new Point(60, 40),
            new Point(60, 70),
            new Point(40, 70)
        };

        // Act
        var expandedPolygon = SampleImageProcessor.EnforceMinimumPolygonFootprint(
            polygon,
            new Size(300, 300));

        // Assert
        var expandedBounds = Cv2.BoundingRect(expandedPolygon);
        Assert.True((expandedBounds.Width * expandedBounds.Height) >= 1000);
        Assert.True(expandedBounds.Height > expandedBounds.Width);
    }

    [Fact]
    public void BalloonizePolygon_PolygonCurvesInward_ReturnsConvexHull()
    {
        // Arrange
        var polygon = new[]
        {
            new Point(10, 10),
            new Point(40, 10),
            new Point(25, 20),
            new Point(40, 40),
            new Point(10, 40)
        };

        // Act
        var balloonizedPolygon = SampleImageProcessor.BalloonizePolygon(
            polygon,
            new Size(300, 300));

        // Assert
        Assert.True(Cv2.IsContourConvex(balloonizedPolygon));
        Assert.True(Math.Abs(Cv2.ContourArea(balloonizedPolygon)) >= Math.Abs(Cv2.ContourArea(polygon)));
    }

    [Fact]
    public void BalloonizePolygon_PolygonNearBounds_KeepsAllPointsInsideBounds()
    {
        // Arrange
        var polygon = new[]
        {
            new Point(0, 5),
            new Point(12, 0),
            new Point(22, 18),
            new Point(3, 24)
        };

        // Act
        var balloonizedPolygon = SampleImageProcessor.BalloonizePolygon(
            polygon,
            new Size(25, 25));

        // Assert
        Assert.All(balloonizedPolygon, point => Assert.InRange(point.X, 0, 24));
        Assert.All(balloonizedPolygon, point => Assert.InRange(point.Y, 0, 24));
    }

    [Fact]
    public void ShouldAttemptMultiPolygonSplit_ContourAreaIsSmall_ReturnsFalse()
    {
        // Arrange
        const double contourArea = 8000;

        // Act
        var shouldSplit = SampleImageProcessor.ShouldAttemptMultiPolygonSplit(contourArea);

        // Assert
        Assert.False(shouldSplit);
    }

    [Fact]
    public void ShouldAttemptSideBySideSplit_ContourIsShort_ReturnsFalse()
    {
        // Arrange
        var contourBounds = new Rect(10, 20, 220, 100);

        // Act
        var shouldSplit = SampleImageProcessor.ShouldAttemptSideBySideSplit(contourBounds);

        // Assert
        Assert.False(shouldSplit);
    }

    [Fact]
    public void ShouldMergeSiblingPolygons_PolygonsAreSimilarSizedPeers_ReturnsFalse()
    {
        // Arrange
        var firstPolygon = new[]
        {
            new Point(20, 20),
            new Point(80, 20),
            new Point(80, 120),
            new Point(20, 120)
        };
        var secondPolygon = new[]
        {
            new Point(90, 24),
            new Point(150, 24),
            new Point(150, 118),
            new Point(90, 118)
        };

        // Act
        var shouldMerge = SampleImageProcessor.ShouldMergeSiblingPolygons(firstPolygon, secondPolygon);

        // Assert
        Assert.False(shouldMerge);
    }

    [Fact]
    public void ShouldMergeSiblingPolygons_SmallArtifactTouchesLargerCluster_ReturnsTrue()
    {
        // Arrange
        var firstPolygon = new[]
        {
            new Point(20, 20),
            new Point(100, 20),
            new Point(100, 120),
            new Point(20, 120)
        };
        var secondPolygon = new[]
        {
            new Point(106, 36),
            new Point(130, 36),
            new Point(130, 82),
            new Point(106, 82)
        };

        // Act
        var shouldMerge = SampleImageProcessor.ShouldMergeSiblingPolygons(firstPolygon, secondPolygon);

        // Assert
        Assert.True(shouldMerge);
    }

    [Fact]
    public void MergeSiblingPolygons_SmallArtifactTouchesLargerCluster_ReturnsSingleMergedPolygon()
    {
        // Arrange
        var sourceContour = new[]
        {
            new Point(10, 10),
            new Point(170, 10),
            new Point(170, 130),
            new Point(10, 130)
        };
        var polygons = new List<Point[]>
        {
            new[]
            {
                new Point(20, 20),
                new Point(100, 20),
                new Point(100, 120),
                new Point(20, 120)
            },
            new[]
            {
                new Point(106, 36),
                new Point(130, 36),
                new Point(130, 82),
                new Point(106, 82)
            }
        };

        // Act
        var mergedPolygons = SampleImageProcessor.MergeSiblingPolygons(
            sourceContour,
            polygons,
            new Size(300, 300));

        // Assert
        Assert.Single(mergedPolygons);
    }

    [Fact]
    public void AnalyzeImageFile_MoreThanEightPolygonsDetected_KeepsOnlyEightLargestPolygons()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var imagePath = Path.Combine(workspace.Path, "many-clusters.png");
        using (var image = new Mat(new Size(1200, 900), MatType.CV_8UC3, Scalar.All(0)))
        {
            using var marker = LoadMarkerImage();
            PasteMarker(image, marker, new Point(100, 150));
            PasteMarker(image, marker, new Point(752, 150));
            PasteMarker(image, marker, new Point(100, 752));
            PasteMarker(image, marker, new Point(752, 752));

            for (var index = 0; index < 9; index++)
            {
                var center = new Point(180 + (index % 3 * 180), 220 + (index / 3 * 180));
                Cv2.Ellipse(image, center, new Size(32, 24), 0, 0, 360, new Scalar(0, 160, 255), -1, LineTypes.AntiAlias);
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

    [Fact]
    public void AnalyzeImageFile_FourSeparatedClustersShareBroadRegion_ReturnsMultiplePolygons()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var imagePath = Path.Combine(workspace.Path, "four-clusters.png");
        SyntheticDiscoveryImageFactory.WriteFourClusterImage(imagePath);
        var processor = new SampleImageProcessor();

        // Act
        var analysis = processor.AnalyzeImageFile(imagePath);

        // Assert
        Assert.True(analysis.PlayfieldDetection.IsFound);
        Assert.True(analysis.Polygons.Count >= 2);
    }

    [Fact]
    public void AnalyzeImageFile_SparseLowerClusterExists_ReturnsRecoveredLowerPolygon()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var imagePath = Path.Combine(workspace.Path, "sparse-lower-cluster.png");
        SyntheticDiscoveryImageFactory.WriteSparseLowerClusterImage(imagePath);
        var processor = new SampleImageProcessor();

        // Act
        var analysis = processor.AnalyzeImageFile(imagePath);

        // Assert
        Assert.True(analysis.PlayfieldDetection.IsFound);
        Assert.True(analysis.Polygons.Count >= 2);
        Assert.Contains(
            analysis.Polygons,
            polygon => Cv2.BoundingRect(polygon).Top > 450);
    }

    [Fact]
    public void AnalyzeImageFile_MultiSizeClustersExist_KeepsSmallerValidPolygons()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var imagePath = Path.Combine(workspace.Path, "multi-size-clusters.png");
        SyntheticDiscoveryImageFactory.WriteMultiSizeClusterImage(imagePath);
        var processor = new SampleImageProcessor();

        // Act
        var analysis = processor.AnalyzeImageFile(imagePath);

        // Assert
        Assert.True(analysis.PlayfieldDetection.IsFound);
        Assert.Equal(4, analysis.Polygons.Count);
        Assert.All(
            analysis.Polygons,
            polygon =>
            {
                var bounds = Cv2.BoundingRect(polygon);
                Assert.True((bounds.Width * bounds.Height) >= 30_000);
            });
        Assert.Contains(
            analysis.Polygons,
            polygon =>
            {
                var bounds = Cv2.BoundingRect(polygon);
                return (bounds.Width * bounds.Height) < 45_000;
            });
    }

    [Fact]
    public void AnalyzeImageFile_OnlyUpperClusterExists_DoesNotAddExtraPolygon()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var imagePath = Path.Combine(workspace.Path, "single-cluster.png");
        SyntheticDiscoveryImageFactory.WriteSingleClusterImage(imagePath);
        var processor = new SampleImageProcessor();

        // Act
        var analysis = processor.AnalyzeImageFile(imagePath);

        // Assert
        Assert.True(analysis.PlayfieldDetection.IsFound);
        Assert.Single(analysis.Polygons);
    }

    private static void CreateSolidImage(string path, int width, int height)
    {
        using var image = new Mat(new Size(width, height), MatType.CV_8UC3, Scalar.All(0));
        Cv2.ImWrite(path, image);
    }

    private static void WriteExpectedOverlay(string samplePath, string expectedPath, IReadOnlyList<Point[]> localPolygons)
    {
        using var sampleImage = Cv2.ImRead(samplePath);
        var detector = new PlayfieldDetector();
        var playfieldDetection = detector.Detect(sampleImage);
        Assert.True(playfieldDetection.IsFound);

        using var expectedImage = sampleImage.Clone();
        using var playfield = new Mat(expectedImage, playfieldDetection.Bounds);
        using var overlay = playfield.Clone();

        foreach (var polygon in localPolygons)
        {
            Cv2.FillPoly(overlay, [polygon], new Scalar(60, 95, 150));
            Cv2.Polylines(overlay, [polygon], true, Scalar.White, 2, LineTypes.AntiAlias);
        }

        Cv2.AddWeighted(overlay, 0.55, playfield, 0.45, 0, playfield);
        Cv2.ImWrite(expectedPath, expectedImage);
    }

    private static void WriteMaskedExpectedOverlay(string samplePath, string maskedExpectedPath, IReadOnlyList<Point[]> localPolygons)
    {
        using var sampleImage = Cv2.ImRead(samplePath);
        var detector = new PlayfieldDetector();
        var playfieldDetection = detector.Detect(sampleImage);
        Assert.True(playfieldDetection.IsFound);

        using var maskedImage = sampleImage.Clone();
        using var grayscale = new Mat();
        Cv2.CvtColor(maskedImage, grayscale, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(grayscale, maskedImage, ColorConversionCodes.GRAY2BGR);

        using var playfield = new Mat(maskedImage, playfieldDetection.Bounds);
        foreach (var polygon in localPolygons)
        {
            Cv2.FillPoly(playfield, [polygon], Scalar.White);
            Cv2.Polylines(playfield, [polygon], true, Scalar.White, 2, LineTypes.AntiAlias);
        }

        Cv2.ImWrite(maskedExpectedPath, maskedImage);
    }

    private static Point[] TranslatePolygon(Point[] polygon, Point offset)
    {
        return polygon
            .Select(point => new Point(point.X + offset.X, point.Y + offset.Y))
            .ToArray();
    }

    private static Point2d GetPolygonCenter(IReadOnlyList<Point> polygon)
    {
        var bounds = Cv2.BoundingRect(polygon);
        return new Point2d(bounds.X + (bounds.Width / 2.0), bounds.Y + (bounds.Height / 2.0));
    }

    private static double Distance(Point2d firstPoint, Point2d secondPoint)
    {
        var deltaX = firstPoint.X - secondPoint.X;
        var deltaY = firstPoint.Y - secondPoint.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static double DistancePointToSegment(Point point, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx == 0 && dy == 0)
        {
            var ddx = point.X - start.X;
            var ddy = point.Y - start.Y;
            return Math.Sqrt((ddx * ddx) + (ddy * ddy));
        }

        var tNumerator = ((point.X - start.X) * dx) + ((point.Y - start.Y) * dy);
        var tDenominator = (dx * dx) + (dy * dy);
        var t = Math.Clamp(tNumerator / (double)tDenominator, 0.0, 1.0);
        var projectionX = start.X + (dx * t);
        var projectionY = start.Y + (dy * t);
        var distanceX = point.X - projectionX;
        var distanceY = point.Y - projectionY;
        return Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY));
    }

    private static Mat LoadMarkerImage()
    {
        using var bitmap = Properties.Resources.marker;
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
        return Cv2.ImDecode(memoryStream.ToArray(), ImreadModes.Color);
    }

    private static void PasteMarker(Mat image, Mat marker, Point location)
    {
        using var roi = new Mat(image, new Rect(location.X, location.Y, marker.Width, marker.Height));
        marker.CopyTo(roi);
    }
}
