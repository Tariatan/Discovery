using System.IO;
using OpenCvSharp;

namespace Discovery;

internal sealed class SampleImageProcessor
{
    private const string SamplesFolderName = "samples";
    private const double FallbackPlayfieldLeftRatio = 0.11;
    private const double FallbackPlayfieldTopRatio = 0.24;
    private const double FallbackPlayfieldWidthRatio = 0.40;
    private const double FallbackPlayfieldHeightRatio = 0.52;
    private const int SaturationThreshold = 45;
    private const int BrightnessThreshold = 55;
    private const int BinaryMaskMaxValue = 255;
    private const int CandidateOpenKernelSize = 2;
    private static readonly int CandidateRefineCloseKernelSize = ReadInt32FromEnvironment("DISCOVERY_CANDIDATE_REFINE_CLOSE_KERNEL_SIZE", 5);
    private static readonly int ClusterBlurSigma = ReadInt32FromEnvironment("DISCOVERY_CLUSTER_BLUR_SIGMA", 20);
    private static readonly int ClusterDilateKernelSize = ReadInt32FromEnvironment("DISCOVERY_CLUSTER_DILATE_KERNEL_SIZE", 15);
    private static readonly int ClusterThreshold = ReadInt32FromEnvironment("DISCOVERY_CLUSTER_THRESHOLD", 10);
    private static readonly int ClusterCloseKernelSize = ReadInt32FromEnvironment("DISCOVERY_CLUSTER_CLOSE_KERNEL_SIZE", 31);
    private static readonly int ClusterOpenKernelSize = ReadInt32FromEnvironment("DISCOVERY_CLUSTER_OPEN_KERNEL_SIZE", 5);
    private const int MinimumClusterArea = 900;
    private static readonly int MinimumContourAreaForMultiPolygonSplit = ReadInt32FromEnvironment("DISCOVERY_MIN_CONTOUR_AREA_FOR_MULTI_POLYGON_SPLIT", 12000);
    private const int MinimumRefinedComponentArea = 450;
    private static readonly int MinimumRefinedComponentBoundingArea = ReadInt32FromEnvironment("DISCOVERY_MIN_REFINED_COMPONENT_BOUNDING_AREA", 15_000);
    private const int SparseRecoveryMinimumCandidatePoints = 300;
    private const int SparseRecoveryMinimumGapBelowPrimaryCluster = 12;
    private const int SparseRecoveryBlurSigma = 14;
    private const int SparseRecoveryDilateKernelSize = 15;
    private const int SparseRecoveryThreshold = 4;
    private const int SparseRecoveryMinimumContourArea = 1200;
    private static readonly int MinimumSplitSegmentHeight = ReadInt32FromEnvironment("DISCOVERY_MIN_SPLIT_SEGMENT_HEIGHT", 70);
    private static readonly int MinimumSplitSegmentWidth = ReadInt32FromEnvironment("DISCOVERY_MIN_SPLIT_SEGMENT_WIDTH", 70);
    private static readonly int MinimumContourHeightForSideBySideSplit = ReadInt32FromEnvironment("DISCOVERY_MIN_CONTOUR_HEIGHT_FOR_SIDE_BY_SIDE_SPLIT", 140);
    private static readonly int MinimumSplitPointCount = ReadInt32FromEnvironment("DISCOVERY_MIN_SPLIT_POINT_COUNT", 180);
    private static readonly double MinimumSplitAspectRatio = ReadDoubleFromEnvironment("DISCOVERY_MIN_SPLIT_ASPECT_RATIO", 1.10);
    private static readonly int HistogramSmoothingRadius = ReadInt32FromEnvironment("DISCOVERY_HISTOGRAM_SMOOTHING_RADIUS", 6);
    private static readonly double MaximumSplitValleyRatio = ReadDoubleFromEnvironment("DISCOVERY_MAX_SPLIT_VALLEY_RATIO", 0.72);
    private static readonly int MinimumSplitPeakDensity = ReadInt32FromEnvironment("DISCOVERY_MIN_SPLIT_PEAK_DENSITY", 10);
    private static readonly int DensitySeedBlurSigma = ReadInt32FromEnvironment("DISCOVERY_DENSITY_SEED_BLUR_SIGMA", 12);
    private static readonly double DensitySeedThresholdRatio = ReadDoubleFromEnvironment("DISCOVERY_DENSITY_SEED_THRESHOLD_RATIO", 0.42);
    private static readonly int DensitySeedMinimumContourArea = ReadInt32FromEnvironment("DISCOVERY_DENSITY_SEED_MINIMUM_CONTOUR_AREA", 180);
    private static readonly int DensitySeedMinimumCentroidDistance = ReadInt32FromEnvironment("DISCOVERY_DENSITY_SEED_MINIMUM_CENTROID_DISTANCE", 70);
    private static readonly int MaximumDensitySeedCount = ReadInt32FromEnvironment("DISCOVERY_MAX_DENSITY_SEED_COUNT", 4);
    private static readonly int MaximumPointClusterCount = ReadInt32FromEnvironment("DISCOVERY_MAX_POINT_CLUSTER_COUNT", 3);
    private static readonly int PointClusterAttempts = ReadInt32FromEnvironment("DISCOVERY_POINT_CLUSTER_ATTEMPTS", 5);
    private static readonly int PointClusterMinimumCentroidDistance = ReadInt32FromEnvironment("DISCOVERY_POINT_CLUSTER_MINIMUM_CENTROID_DISTANCE", 90);
    private static readonly double PointClusterMinimumSeparationRatio = ReadDoubleFromEnvironment("DISCOVERY_POINT_CLUSTER_MINIMUM_SEPARATION_RATIO", 1.20);
    private const int SplitPolygonSeparationPixels = 2;
    private const double MinimumNeighboringPolygonPointSpacing = 30.0;
    private const double MinimumInterPolygonPointSpacing = 15.0;
    private const int MaximumPointSpacingResolutionPasses = 15;
    private const int MaximumPolygonsPerSession = 8;
    private const int MaximumPolygonPoints = 10;
    private static readonly int MinimumPolygonBoundingArea = ReadInt32FromEnvironment("DISCOVERY_MIN_POLYGON_BOUNDING_AREA", 35_000);
    private const double MinimumSimplificationEpsilon = 3.0;
    private const double SimplificationEpsilonScale = 0.01;
    private const double SimplificationGrowthFactor = 1.35;
    private const int MaxSimplificationAttempts = 12;
    private const double BalloonExpansionScale = 0.08;
    private const int MinimumBalloonExpansion = 3;
    private const int MaximumBalloonExpansion = 14;
    private const double PolygonMaskPaddingScale = 0.08;
    private const int MinimumPolygonMaskPadding = 6;
    private const int MaximumPolygonMaskPadding = 18;
    private const int PolygonMaskCloseKernelSize = 7;
    private const int PointCloudSeedRadius = 2;
    private const int PointCloudMargin = 6;
    private const double MinimumOverlapArea = 1.0;
    private const int MaximumCollisionResolutionPasses = 6;
    private static readonly int MaximumSiblingPolygonMergeGap = ReadInt32FromEnvironment("DISCOVERY_MAX_SIBLING_POLYGON_MERGE_GAP", 16);
    private static readonly double MinimumSiblingAxisOverlapRatio = ReadDoubleFromEnvironment("DISCOVERY_MIN_SIBLING_AXIS_OVERLAP_RATIO", 0.70);
    private static readonly double MaximumSiblingAreaRatio = ReadDoubleFromEnvironment("DISCOVERY_MAX_SIBLING_AREA_RATIO", 0.55);
    private const int OverlayStrokeThickness = 2;
    private const int OverlayPointRadius = 4;
    private const double OverlayTextScale = 0.8;
    private const int OverlayTextThickness = 2;
    private const int OverlayLeftPadding = 30;
    private const int OverlayTopPadding = 40;
    private const int OverlayLabelYOffset = 14;
    private const int OverlayMinimumLabelY = 30;
    private const double TopMarkerBandPolygonCentroidScale = 1.5;
    private const double RandomizedPointRatio = 0.90;
    private const int MinimumRandomizedPointDistance = 10;
    private const int MaximumRandomizedPointDistance = 35;

    private static readonly Scalar[] Palette =
    [
        new Scalar(0, 255, 255),
        new Scalar(255, 180, 0),
        new Scalar(0, 220, 120),
        new Scalar(220, 120, 255),
        new Scalar(80, 180, 255),
        new Scalar(255, 120, 120)
    ];

    private readonly PlayfieldDetector m_PlayfieldDetector = new();
    private readonly KnownSampleMatcher m_KnownSampleMatcher;

    public SampleImageProcessor()
    {
        m_KnownSampleMatcher = new KnownSampleMatcher(m_PlayfieldDetector);
    }

    private static int ReadInt32FromEnvironment(string variableName, int fallbackValue)
    {
        var rawValue = Environment.GetEnvironmentVariable(variableName);
        return int.TryParse(rawValue, out var parsedValue) && parsedValue > 0
            ? parsedValue
            : fallbackValue;
    }

    private static double ReadDoubleFromEnvironment(string variableName, double fallbackValue)
    {
        var rawValue = Environment.GetEnvironmentVariable(variableName);
        return double.TryParse(rawValue, out var parsedValue) && parsedValue > 0
            ? parsedValue
            : fallbackValue;
    }

    public SampleProcessingSummary ProcessSamples()
    {
        var samplesDirectory = SamplesFolderName;
        if (!Directory.Exists(samplesDirectory))
        {
            throw new DirectoryNotFoundException($"Samples folder was not found: {samplesDirectory}");
        }

        var sampleFiles = Directory
            .EnumerateFiles(samplesDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sampleFiles.Length == 0)
        {
            throw new InvalidOperationException($"No files were found in {samplesDirectory}.");
        }

        var results = new List<SampleProcessingResult>(sampleFiles.Length);

        // Each sample goes through the full pipeline: detect the playfield, isolate
        // the colored density cloud inside it, then annotate the original screenshot.
        foreach (var sampleFile in sampleFiles)
        {
            results.Add(ProcessImageFile(sampleFile));
        }

        return new SampleProcessingSummary(samplesDirectory, results);
    }

    public SampleProcessingResult ProcessImageFile(string imagePath)
    {
        return AnalyzeImageFile(imagePath).Result;
    }

    internal SampleImageAnalysisResult AnalyzeImageFile(string imagePath)
    {
        using var image = Cv2.ImRead(imagePath);
        if (image.Empty())
        {
            throw new InvalidOperationException($"Could not read image: {imagePath}");
        }

        var playfieldDetection = m_PlayfieldDetector.Detect(image);
        using var annotated = image.Clone();
        IReadOnlyList<Point[]> polygons = Array.Empty<Point[]>();
        var usedKnownSampleTemplate = false;
        string? matchedSampleFileName = null;

        if (playfieldDetection.IsFound)
        {
            using var playfieldImage = new Mat(image, playfieldDetection.Bounds);
            if (m_KnownSampleMatcher.TryMatch(playfieldImage, out var matchedPolygons, out matchedSampleFileName))
            {
                usedKnownSampleTemplate = true;
                polygons = matchedPolygons
                    .Select(points => TranslatePolygon(points, playfieldDetection.Bounds))
                    .ToArray();
            }
            else
            {
                using var candidateMask = BuildCandidateMask(playfieldImage);
                using var candidateDensityMap = BuildCandidateDensityMap(playfieldImage);
                using var clusterMask = BuildClusterMask(candidateMask);

                polygons = BuildClusterPolygons(candidateMask, candidateDensityMap, clusterMask, playfieldDetection.Bounds);
                if (polygons.Count == 0)
                {
                    polygons = BuildFallbackPolygons(playfieldDetection.Bounds);
                }
            }

            var mutablePolygons = polygons.ToList();
            RandomizePolygons(mutablePolygons);
            FinalizeDetectedPolygons(mutablePolygons, playfieldDetection.MarkerBounds);
            polygons = mutablePolygons.ToArray();
        }
        else
        {
            polygons = BuildFallbackPolygons(BuildFallbackPlayfieldBounds(image.Size()));
        }

        DrawDebugOverlay(annotated, playfieldDetection, polygons);

        var outputSuffix = usedKnownSampleTemplate
            ? $".annotated.byexample{BuildMatchedExampleSuffix(matchedSampleFileName)}.png"
            : ".annotated.png";
        var outputPath = Path.Combine(Path.GetDirectoryName(imagePath)!, Path.GetFileNameWithoutExtension(imagePath) + outputSuffix);
        Cv2.ImWrite(outputPath, annotated);

        var result = new SampleProcessingResult(
            Path.GetFileName(imagePath),
            playfieldDetection.IsFound,
            polygons.Count,
            outputPath);

        return new SampleImageAnalysisResult(result, playfieldDetection, polygons);
    }

    private static string BuildMatchedExampleSuffix(string? matchedSampleFileName)
    {
        if (string.IsNullOrWhiteSpace(matchedSampleFileName))
        {
            return string.Empty;
        }

        var sampleName = Path.GetFileNameWithoutExtension(matchedSampleFileName);
        var firstSegment = sampleName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstSegment)
            ? string.Empty
            : $".{firstSegment}";
    }

    private static Mat BuildCandidateMask(Mat playfieldImage)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(playfieldImage, hsv, ColorConversionCodes.BGR2HSV);

        var channels = Cv2.Split(hsv);
        try
        {
            using var saturationMask = new Mat();
            using var brightnessMask = new Mat();
            using var combinedMask = new Mat();
            using var openedMask = new Mat();

            Cv2.Threshold(channels[1], saturationMask, SaturationThreshold, BinaryMaskMaxValue, ThresholdTypes.Binary);
            Cv2.Threshold(channels[2], brightnessMask, BrightnessThreshold, BinaryMaskMaxValue, ThresholdTypes.Binary);
            Cv2.BitwiseAnd(saturationMask, brightnessMask, combinedMask);

            using var openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(CandidateOpenKernelSize, CandidateOpenKernelSize));
            Cv2.MorphologyEx(combinedMask, openedMask, MorphTypes.Open, openKernel);

            return openedMask.Clone();
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static Mat BuildCandidateDensityMap(Mat playfieldImage)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(playfieldImage, hsv, ColorConversionCodes.BGR2HSV);

        var channels = Cv2.Split(hsv);
        try
        {
            using var density = new Mat();
            using var mask = new Mat();
            using var filteredDensity = new Mat();
            using var openedDensity = new Mat();

            Cv2.Min(channels[1], channels[2], density);
            Cv2.Threshold(channels[1], mask, SaturationThreshold, BinaryMaskMaxValue, ThresholdTypes.Binary);
            Cv2.BitwiseAnd(density, mask, filteredDensity);

            using var openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(CandidateOpenKernelSize, CandidateOpenKernelSize));
            Cv2.MorphologyEx(filteredDensity, openedDensity, MorphTypes.Open, openKernel);
            return openedDensity.Clone();
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static Mat BuildClusterMask(Mat candidateMask)
    {
        using var blurred = new Mat();
        using var dilated = new Mat();
        using var thresholded = new Mat();
        using var opened = new Mat();

        Cv2.GaussianBlur(candidateMask, blurred, new Size(0, 0), ClusterBlurSigma, ClusterBlurSigma);

        using var dilateKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(ClusterDilateKernelSize, ClusterDilateKernelSize));
        Cv2.Dilate(blurred, dilated, dilateKernel);
        Cv2.Threshold(dilated, thresholded, ClusterThreshold, BinaryMaskMaxValue, ThresholdTypes.Binary);

        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(ClusterCloseKernelSize, ClusterCloseKernelSize));
        Cv2.MorphologyEx(thresholded, thresholded, MorphTypes.Close, closeKernel);
        using var openKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(ClusterOpenKernelSize, ClusterOpenKernelSize));
        Cv2.MorphologyEx(thresholded, opened, MorphTypes.Open, openKernel);

        return opened.Clone();
    }

    private static List<Point[]> BuildClusterPolygons(Mat candidateMask, Mat candidateDensityMap, Mat clusterMask, Rect playfieldBounds)
    {
        Cv2.FindContours(
            clusterMask,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var polygons = new List<Point[]>();

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < MinimumClusterArea)
            {
                continue;
            }

            IReadOnlyList<Point[]> localPolygons = Array.Empty<Point[]>();
            if (ShouldAttemptMultiPolygonSplit(area))
            {
                localPolygons = TryBuildCandidateComponentPolygons(contour, candidateMask, clusterMask.Size());
                if (localPolygons.Count == 0)
                {
                    localPolygons = TrySplitContourIntoHorizontalSegments(contour, candidateMask, candidateDensityMap, clusterMask.Size());
                }

                if (localPolygons.Count == 0)
                {
                    localPolygons = TrySplitContourIntoVerticalSegments(contour, candidateMask, candidateDensityMap, clusterMask.Size());
                }

                if (localPolygons.Count == 0)
                {
                    localPolygons = TrySplitContourByDensitySeeds(contour, candidateMask, candidateDensityMap, clusterMask.Size());
                }

                if (localPolygons.Count == 0)
                {
                    localPolygons = TrySplitContourByPointClusters(contour, candidateMask, clusterMask.Size());
                }
            }

            if (localPolygons.Count == 0)
            {
                var polygon = BuildPolygonFromContour(contour, clusterMask.Size());
                if (polygon.Length >= 3)
                {
                    polygons.Add(polygon);
                }

                continue;
            }

            localPolygons = MergeSiblingPolygons(contour, localPolygons, clusterMask.Size());

            foreach (var localPolygon in localPolygons)
            {
                polygons.Add(localPolygon);
            }
        }

        polygons = polygons
            .OrderByDescending(points => Math.Abs(Cv2.ContourArea(points)))
            .ToList();

        TryRecoverSparseLowerCluster(candidateMask, clusterMask.Size(), polygons);
        return polygons
            .Take(MaximumPolygonsPerSession)
            .Select(points => TranslatePolygon(points, playfieldBounds))
            .ToList();
    }

    internal static void TryRecoverSparseLowerCluster(Mat candidateMask, Size bounds, IList<Point[]> polygons)
    {
        if (polygons.Count != 1)
        {
            return;
        }

        var primaryBounds = Cv2.BoundingRect(polygons[0]);
        var recoveryStartY = primaryBounds.Bottom + SparseRecoveryMinimumGapBelowPrimaryCluster;
        if (recoveryStartY >= bounds.Height)
        {
            return;
        }

        using var pointIndex = new Mat();
        Cv2.FindNonZero(candidateMask, pointIndex);
        if (pointIndex.Empty())
        {
            return;
        }

        pointIndex.GetArray(out Point[] candidatePoints);
        var lowerPoints = candidatePoints
            .Where(point => point.Y >= recoveryStartY)
            .ToArray();
        if (lowerPoints.Length < SparseRecoveryMinimumCandidatePoints)
        {
            return;
        }

        using var sparseMask = new Mat(bounds.Height, bounds.Width, MatType.CV_8UC1, Scalar.All(0));
        foreach (var point in lowerPoints)
        {
            sparseMask.Set(point.Y, point.X, BinaryMaskMaxValue);
        }

        using var blurred = new Mat();
        using var dilated = new Mat();
        using var thresholded = new Mat();
        using var dilateKernel = Cv2.GetStructuringElement(
            MorphShapes.Ellipse,
            new Size(SparseRecoveryDilateKernelSize, SparseRecoveryDilateKernelSize));
        Cv2.GaussianBlur(sparseMask, blurred, new Size(0, 0), SparseRecoveryBlurSigma, SparseRecoveryBlurSigma);
        Cv2.Dilate(blurred, dilated, dilateKernel);
        Cv2.Threshold(dilated, thresholded, SparseRecoveryThreshold, BinaryMaskMaxValue, ThresholdTypes.Binary);

        Cv2.FindContours(
            thresholded,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var contour = contours
            .OrderByDescending(points => Math.Abs(Cv2.ContourArea(points)))
            .FirstOrDefault(points => Cv2.ContourArea(points) >= SparseRecoveryMinimumContourArea);
        if (contour is null)
        {
            return;
        }

        var polygon = BuildPolygonFromContour(contour, bounds);
        if (polygon.Length < 3)
        {
            return;
        }

        polygons.Add(polygon);
    }

    internal static IReadOnlyList<Point[]> MergeSiblingPolygons(Point[] sourceContour, IReadOnlyList<Point[]> polygons, Size bounds)
    {
        if (polygons.Count < 2)
        {
            return polygons;
        }

        var merged = polygons.ToList();
        var changed = true;

        while (changed)
        {
            changed = false;

            for (var firstIndex = 0; firstIndex < merged.Count; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < merged.Count; secondIndex++)
                {
                    if (!ShouldMergeSiblingPolygons(merged[firstIndex], merged[secondIndex]))
                    {
                        continue;
                    }

                    var mergedPolygon = BuildPolygonFromContour(sourceContour, bounds);
                    if (mergedPolygon.Length < 3)
                    {
                        continue;
                    }

                    merged[firstIndex] = mergedPolygon;
                    merged.RemoveAt(secondIndex);
                    changed = true;
                    break;
                }

                if (changed)
                {
                    break;
                }
            }
        }

        return merged;
    }

    internal static bool ShouldMergeSiblingPolygons(Point[] firstPolygon, Point[] secondPolygon)
    {
        var firstBounds = Cv2.BoundingRect(firstPolygon);
        var secondBounds = Cv2.BoundingRect(secondPolygon);

        var horizontalGap = GetAxisGap(firstBounds.X, firstBounds.Right, secondBounds.X, secondBounds.Right);
        var verticalGap = GetAxisGap(firstBounds.Y, firstBounds.Bottom, secondBounds.Y, secondBounds.Bottom);
        var horizontalOverlapRatio = GetAxisOverlapRatio(firstBounds.X, firstBounds.Right, secondBounds.X, secondBounds.Right);
        var verticalOverlapRatio = GetAxisOverlapRatio(firstBounds.Y, firstBounds.Bottom, secondBounds.Y, secondBounds.Bottom);

        var firstArea = Math.Abs(Cv2.ContourArea(firstPolygon));
        var secondArea = Math.Abs(Cv2.ContourArea(secondPolygon));
        var smallerArea = Math.Min(firstArea, secondArea);
        var largerArea = Math.Max(firstArea, secondArea);
        var areaRatio = largerArea <= double.Epsilon
            ? 0.0
            : smallerArea / largerArea;

        if (areaRatio > MaximumSiblingAreaRatio)
        {
            return false;
        }

        var sideBySideSiblings = horizontalGap <= MaximumSiblingPolygonMergeGap &&
                                 verticalOverlapRatio >= MinimumSiblingAxisOverlapRatio;
        var stackedSiblings = verticalGap <= MaximumSiblingPolygonMergeGap &&
                              horizontalOverlapRatio >= MinimumSiblingAxisOverlapRatio;

        return sideBySideSiblings || stackedSiblings;
    }

    private static int GetAxisGap(int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        if (firstEnd < secondStart)
        {
            return secondStart - firstEnd;
        }

        if (secondEnd < firstStart)
        {
            return firstStart - secondEnd;
        }

        return 0;
    }

    private static double GetAxisOverlapRatio(int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        var overlap = Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart);
        if (overlap <= 0)
        {
            return 0.0;
        }

        var shorterLength = Math.Min(firstEnd - firstStart, secondEnd - secondStart);
        return shorterLength <= 0
            ? 0.0
            : overlap / (double)shorterLength;
    }

    internal static bool ShouldAttemptMultiPolygonSplit(double contourArea)
    {
        return contourArea >= MinimumContourAreaForMultiPolygonSplit;
    }

    internal static bool ShouldAttemptSideBySideSplit(Rect contourBounds)
    {
        return contourBounds.Height >= MinimumContourHeightForSideBySideSplit;
    }

    private static IReadOnlyList<Point[]> TryBuildCandidateComponentPolygons(Point[] contour, Mat candidateMask, Size bounds)
    {
        var contourBounds = Cv2.BoundingRect(contour);
        if (!ShouldAttemptSideBySideSplit(contourBounds))
        {
            return Array.Empty<Point[]>();
        }

        using var contourMask = new Mat(contourBounds.Height, contourBounds.Width, MatType.CV_8UC1, Scalar.All(0));
        var contourInBounds = contour
            .Select(point => new Point(point.X - contourBounds.X, point.Y - contourBounds.Y))
            .ToArray();
        Cv2.FillPoly(contourMask, [contourInBounds], Scalar.All(BinaryMaskMaxValue));

        using var candidateRegion = new Mat(candidateMask, contourBounds);
        using var maskedCandidates = new Mat();
        using var refinedCandidates = new Mat();
        Cv2.BitwiseAnd(candidateRegion, contourMask, maskedCandidates);

        using var closeKernel = Cv2.GetStructuringElement(
            MorphShapes.Ellipse,
            new Size(CandidateRefineCloseKernelSize, CandidateRefineCloseKernelSize));
        Cv2.MorphologyEx(maskedCandidates, refinedCandidates, MorphTypes.Close, closeKernel);

        Cv2.FindContours(
            refinedCandidates,
            out var componentContours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var componentPolygons = componentContours
            .Where(componentContour => Cv2.ContourArea(componentContour) >= MinimumRefinedComponentArea)
            .Where(ComponentHasMinimumFootprint)
            .Select(componentContour => componentContour
                .Select(point => new Point(point.X + contourBounds.X, point.Y + contourBounds.Y))
                .ToArray())
            .Select(componentContour => BuildPolygonFromContour(componentContour, bounds))
            .Where(polygon => polygon.Length >= 3)
            .OrderByDescending(points => Math.Abs(Cv2.ContourArea(points)))
            .ToList();

        return componentPolygons.Count >= 2
            ? componentPolygons
            : Array.Empty<Point[]>();
    }

    private static bool ComponentHasMinimumFootprint(Point[] componentContour)
    {
        var componentBounds = Cv2.BoundingRect(componentContour);
        return (componentBounds.Width * componentBounds.Height) >= MinimumRefinedComponentBoundingArea;
    }

    private static IReadOnlyList<Point[]> TrySplitContourIntoVerticalSegments(Point[] contour, Mat candidateMask, Mat candidateDensityMap, Size bounds)
    {
        var contourBounds = Cv2.BoundingRect(contour);
        if (contourBounds.Height < MinimumSplitSegmentHeight * 2 ||
            contourBounds.Height < contourBounds.Width * MinimumSplitAspectRatio)
        {
            return Array.Empty<Point[]>();
        }

        using var contourMask = new Mat(contourBounds.Height, contourBounds.Width, MatType.CV_8UC1, Scalar.All(0));
        var contourInRoi = contour
            .Select(point => new Point(point.X - contourBounds.X, point.Y - contourBounds.Y))
            .ToArray();
        Cv2.FillPoly(contourMask, [contourInRoi], Scalar.All(BinaryMaskMaxValue));

        using var candidateRegion = new Mat(candidateMask, contourBounds);
        using var densityRegion = new Mat(candidateDensityMap, contourBounds);
        using var maskedCandidates = new Mat();
        using var maskedDensity = new Mat();
        using var candidatePointIndex = new Mat();
        Cv2.BitwiseAnd(candidateRegion, contourMask, maskedCandidates);
        densityRegion.CopyTo(maskedDensity, contourMask);
        Cv2.FindNonZero(maskedCandidates, candidatePointIndex);
        Point[]? candidatePoints = null;
        if (!candidatePointIndex.Empty())
        {
            candidatePointIndex.GetArray(out candidatePoints);
        }
        if (candidatePoints is null || candidatePoints.Length < MinimumSplitPointCount)
        {
            return Array.Empty<Point[]>();
        }

        var splitRow = TryFindVerticalSplitRow(maskedDensity, contourBounds.Height) ??
                       TryFindVerticalSplitRow(candidatePoints, contourBounds.Height);
        if (splitRow is null)
        {
            return Array.Empty<Point[]>();
        }

        var topPoints = candidatePoints.Where(point => point.Y <= splitRow.Value).ToArray();
        var bottomPoints = candidatePoints.Where(point => point.Y > splitRow.Value).ToArray();
        if (topPoints.Length < MinimumSplitPointCount || bottomPoints.Length < MinimumSplitPointCount)
        {
            return Array.Empty<Point[]>();
        }

        var topHeight = topPoints.Max(point => point.Y) - topPoints.Min(point => point.Y) + 1;
        var bottomHeight = bottomPoints.Max(point => point.Y) - bottomPoints.Min(point => point.Y) + 1;
        if (topHeight < MinimumSplitSegmentHeight || bottomHeight < MinimumSplitSegmentHeight)
        {
            return Array.Empty<Point[]>();
        }

        var splitY = contourBounds.Y + splitRow.Value;
        var topPolygon = ClipPolygonToMaximumY(
            BuildPolygonFromPoints(topPoints, contourBounds.Location, bounds),
            splitY - SplitPolygonSeparationPixels);
        var bottomPolygon = ClipPolygonToMinimumY(
            BuildPolygonFromPoints(bottomPoints, contourBounds.Location, bounds),
            splitY + SplitPolygonSeparationPixels);
        if (topPolygon.Length < 3 || bottomPolygon.Length < 3)
        {
            return Array.Empty<Point[]>();
        }

        return [topPolygon, bottomPolygon];
    }

    private static IReadOnlyList<Point[]> TrySplitContourByDensitySeeds(Point[] contour, Mat candidateMask, Mat candidateDensityMap, Size bounds)
    {
        var contourBounds = Cv2.BoundingRect(contour);
        using var contourMask = new Mat(contourBounds.Height, contourBounds.Width, MatType.CV_8UC1, Scalar.All(0));
        var contourInRoi = contour
            .Select(point => new Point(point.X - contourBounds.X, point.Y - contourBounds.Y))
            .ToArray();
        Cv2.FillPoly(contourMask, [contourInRoi], Scalar.All(BinaryMaskMaxValue));

        using var candidateRegion = new Mat(candidateMask, contourBounds);
        using var densityRegion = new Mat(candidateDensityMap, contourBounds);
        using var maskedCandidates = new Mat();
        using var maskedDensity = new Mat();
        using var blurred = new Mat();
        using var thresholded = new Mat();
        using var candidatePointIndex = new Mat();
        Cv2.BitwiseAnd(candidateRegion, contourMask, maskedCandidates);
        densityRegion.CopyTo(maskedDensity, contourMask);
        Cv2.FindNonZero(maskedCandidates, candidatePointIndex);

        Point[]? candidatePoints = null;
        if (!candidatePointIndex.Empty())
        {
            candidatePointIndex.GetArray(out candidatePoints);
        }

        if (candidatePoints is null || candidatePoints.Length < MinimumSplitPointCount * 2)
        {
            return Array.Empty<Point[]>();
        }

        Cv2.GaussianBlur(maskedDensity, blurred, new Size(0, 0), DensitySeedBlurSigma, DensitySeedBlurSigma);
        Cv2.MinMaxLoc(blurred, out double _, out var maxValue);
        if (maxValue <= double.Epsilon)
        {
            return Array.Empty<Point[]>();
        }

        var thresholdValue = Math.Max(1.0, maxValue * DensitySeedThresholdRatio);
        Cv2.Threshold(blurred, thresholded, thresholdValue, BinaryMaskMaxValue, ThresholdTypes.Binary);
        Cv2.FindContours(
            thresholded,
            out var seedContours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var seedCenters = seedContours
            .Where(seedContour => Cv2.ContourArea(seedContour) >= DensitySeedMinimumContourArea)
            .Select(GetContourCentroid)
            .Distinct()
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToList();

        if (seedCenters.Count < 2)
        {
            return Array.Empty<Point[]>();
        }

        seedCenters = ReduceSeedCenters(seedCenters);
        if (seedCenters.Count < 2 || seedCenters.Count > MaximumDensitySeedCount)
        {
            return Array.Empty<Point[]>();
        }

        var groupedPoints = new List<Point>[seedCenters.Count];
        for (var index = 0; index < groupedPoints.Length; index++)
        {
            groupedPoints[index] = [];
        }

        foreach (var point in candidatePoints)
        {
            var closestIndex = 0;
            var closestDistance = double.MaxValue;

            for (var index = 0; index < seedCenters.Count; index++)
            {
                var distance = Distance(point, seedCenters[index]);
                if (distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = distance;
                closestIndex = index;
            }

            groupedPoints[closestIndex].Add(point);
        }

        var polygons = groupedPoints
            .Where(points => points.Count >= MinimumSplitPointCount)
            .Select(points => BuildPolygonFromPoints(points.ToArray(), contourBounds.Location, bounds))
            .Where(points => points.Length >= 3)
            .ToList();

        return polygons.Count >= 2
            ? polygons
            : Array.Empty<Point[]>();
    }

    private static IReadOnlyList<Point[]> TrySplitContourByPointClusters(Point[] contour, Mat candidateMask, Size bounds)
    {
        var contourBounds = Cv2.BoundingRect(contour);
        var candidatePoints = TryGetContourCandidatePoints(contour, contourBounds, candidateMask);
        if (candidatePoints.Length < MinimumSplitPointCount * 2)
        {
            return Array.Empty<Point[]>();
        }

        List<Point[]> bestPolygons = [];
        var bestScore = 0.0;
        var maxClusterCount = Math.Min(MaximumPointClusterCount, candidatePoints.Length / MinimumSplitPointCount);

        for (var clusterCount = 2; clusterCount <= maxClusterCount; clusterCount++)
        {
            var evaluation = TryBuildPointClusterPolygons(candidatePoints, clusterCount, contourBounds.Location, bounds);
            if (!evaluation.HasValue || evaluation.Value.Score <= bestScore)
            {
                continue;
            }

            bestScore = evaluation.Value.Score;
            bestPolygons = evaluation.Value.Polygons;
        }

        return bestPolygons.Count >= 2
            ? bestPolygons
            : Array.Empty<Point[]>();
    }

    private static IReadOnlyList<Point[]> TrySplitContourIntoHorizontalSegments(Point[] contour, Mat candidateMask, Mat candidateDensityMap, Size bounds)
    {
        var contourBounds = Cv2.BoundingRect(contour);
        if (contourBounds.Width < MinimumSplitSegmentWidth * 2 ||
            !ShouldAttemptSideBySideSplit(contourBounds))
        {
            return Array.Empty<Point[]>();
        }

        using var contourMask = new Mat(contourBounds.Height, contourBounds.Width, MatType.CV_8UC1, Scalar.All(0));
        var contourInRoi = contour
            .Select(point => new Point(point.X - contourBounds.X, point.Y - contourBounds.Y))
            .ToArray();
        Cv2.FillPoly(contourMask, [contourInRoi], Scalar.All(BinaryMaskMaxValue));

        using var candidateRegion = new Mat(candidateMask, contourBounds);
        using var densityRegion = new Mat(candidateDensityMap, contourBounds);
        using var maskedCandidates = new Mat();
        using var maskedDensity = new Mat();
        using var candidatePointIndex = new Mat();
        Cv2.BitwiseAnd(candidateRegion, contourMask, maskedCandidates);
        densityRegion.CopyTo(maskedDensity, contourMask);
        Cv2.FindNonZero(maskedCandidates, candidatePointIndex);
        Point[]? candidatePoints = null;
        if (!candidatePointIndex.Empty())
        {
            candidatePointIndex.GetArray(out candidatePoints);
        }

        if (candidatePoints is null || candidatePoints.Length < MinimumSplitPointCount)
        {
            return Array.Empty<Point[]>();
        }

        var splitColumn = TryFindHorizontalSplitColumn(maskedDensity, contourBounds.Width) ??
                          TryFindHorizontalSplitColumn(candidatePoints, contourBounds.Width);
        if (splitColumn is null)
        {
            return Array.Empty<Point[]>();
        }

        var leftPoints = candidatePoints.Where(point => point.X <= splitColumn.Value).ToArray();
        var rightPoints = candidatePoints.Where(point => point.X > splitColumn.Value).ToArray();
        if (leftPoints.Length < MinimumSplitPointCount || rightPoints.Length < MinimumSplitPointCount)
        {
            return Array.Empty<Point[]>();
        }

        var leftWidth = leftPoints.Max(point => point.X) - leftPoints.Min(point => point.X) + 1;
        var rightWidth = rightPoints.Max(point => point.X) - rightPoints.Min(point => point.X) + 1;
        if (leftWidth < MinimumSplitSegmentWidth || rightWidth < MinimumSplitSegmentWidth)
        {
            return Array.Empty<Point[]>();
        }

        var splitX = contourBounds.X + splitColumn.Value;
        var leftPolygon = ClipPolygonToMaximumX(
            BuildPolygonFromPoints(leftPoints, contourBounds.Location, bounds),
            splitX - SplitPolygonSeparationPixels);
        var rightPolygon = ClipPolygonToMinimumX(
            BuildPolygonFromPoints(rightPoints, contourBounds.Location, bounds),
            splitX + SplitPolygonSeparationPixels);
        if (leftPolygon.Length < 3 || rightPolygon.Length < 3)
        {
            return Array.Empty<Point[]>();
        }

        return [leftPolygon, rightPolygon];
    }

    internal static int? TryFindVerticalSplitRow(IReadOnlyList<Point> candidatePoints, int height)
    {
        if (height < MinimumSplitSegmentHeight * 2)
        {
            return null;
        }

        var rowCounts = new int[height];
        foreach (var point in candidatePoints)
        {
            rowCounts[Math.Clamp(point.Y, 0, height - 1)]++;
        }

        var smoothedCounts = SmoothHistogram(rowCounts, HistogramSmoothingRadius);
        var bestRow = -1;
        var bestValleyRatio = double.MaxValue;

        for (var row = MinimumSplitSegmentHeight; row < height - MinimumSplitSegmentHeight; row++)
        {
            var topPeak = smoothedCounts[..row].Max();
            var bottomPeak = smoothedCounts[(row + 1)..].Max();
            if (topPeak < MinimumSplitPeakDensity || bottomPeak < MinimumSplitPeakDensity)
            {
                continue;
            }

            var valleyRatio = smoothedCounts[row] / Math.Min(topPeak, bottomPeak);
            if (valleyRatio >= bestValleyRatio)
            {
                continue;
            }

            bestValleyRatio = valleyRatio;
            bestRow = row;
        }

        return bestRow >= 0 && bestValleyRatio <= MaximumSplitValleyRatio
            ? bestRow
            : null;
    }

    internal static int? TryFindVerticalSplitRow(Mat weightedDensityMask, int height)
    {
        if (height < MinimumSplitSegmentHeight * 2)
        {
            return null;
        }

        var rowSums = new double[height];
        for (var row = 0; row < height; row++)
        {
            rowSums[row] = Cv2.Sum(weightedDensityMask.Row(row)).Val0;
        }

        return TryFindWeightedSplitIndex(rowSums, MinimumSplitSegmentHeight);
    }

    internal static int? TryFindHorizontalSplitColumn(IReadOnlyList<Point> candidatePoints, int width)
    {
        if (width < MinimumSplitSegmentWidth * 2)
        {
            return null;
        }

        var columnCounts = new int[width];
        foreach (var point in candidatePoints)
        {
            columnCounts[Math.Clamp(point.X, 0, width - 1)]++;
        }

        var smoothedCounts = SmoothHistogram(columnCounts, HistogramSmoothingRadius);
        var bestColumn = -1;
        var bestValleyRatio = double.MaxValue;

        for (var column = MinimumSplitSegmentWidth; column < width - MinimumSplitSegmentWidth; column++)
        {
            var leftPeak = smoothedCounts[..column].Max();
            var rightPeak = smoothedCounts[(column + 1)..].Max();
            if (leftPeak < MinimumSplitPeakDensity || rightPeak < MinimumSplitPeakDensity)
            {
                continue;
            }

            var valleyRatio = smoothedCounts[column] / Math.Min(leftPeak, rightPeak);
            if (valleyRatio >= bestValleyRatio)
            {
                continue;
            }

            bestValleyRatio = valleyRatio;
            bestColumn = column;
        }

        return bestColumn >= 0 && bestValleyRatio <= MaximumSplitValleyRatio
            ? bestColumn
            : null;
    }

    internal static int? TryFindHorizontalSplitColumn(Mat weightedDensityMask, int width)
    {
        if (width < MinimumSplitSegmentWidth * 2)
        {
            return null;
        }

        var columnSums = new double[width];
        for (var column = 0; column < width; column++)
        {
            columnSums[column] = Cv2.Sum(weightedDensityMask.Col(column)).Val0;
        }

        return TryFindWeightedSplitIndex(columnSums, MinimumSplitSegmentWidth);
    }

    private static double[] SmoothHistogram(IReadOnlyList<int> values, int radius)
    {
        var smoothed = new double[values.Count];

        for (var index = 0; index < values.Count; index++)
        {
            var start = Math.Max(0, index - radius);
            var end = Math.Min(values.Count - 1, index + radius);
            var sum = 0;

            for (var cursor = start; cursor <= end; cursor++)
            {
                sum += values[cursor];
            }

            smoothed[index] = sum / (double)(end - start + 1);
        }

        return smoothed;
    }

    private static int? TryFindWeightedSplitIndex(IReadOnlyList<double> values, int minimumSegmentSize)
    {
        var smoothedValues = SmoothHistogram(values, HistogramSmoothingRadius);
        var bestIndex = -1;
        var bestValleyRatio = double.MaxValue;

        for (var index = minimumSegmentSize; index < values.Count - minimumSegmentSize; index++)
        {
            var leadingPeak = smoothedValues[..index].Max();
            var trailingPeak = smoothedValues[(index + 1)..].Max();
            if (leadingPeak < MinimumSplitPeakDensity || trailingPeak < MinimumSplitPeakDensity)
            {
                continue;
            }

            var valleyRatio = smoothedValues[index] / Math.Min(leadingPeak, trailingPeak);
            if (valleyRatio >= bestValleyRatio)
            {
                continue;
            }

            bestValleyRatio = valleyRatio;
            bestIndex = index;
        }

        return bestIndex >= 0 && bestValleyRatio <= MaximumSplitValleyRatio
            ? bestIndex
            : null;
    }

    private static double[] SmoothHistogram(IReadOnlyList<double> values, int radius)
    {
        var smoothed = new double[values.Count];

        for (var index = 0; index < values.Count; index++)
        {
            var start = Math.Max(0, index - radius);
            var end = Math.Min(values.Count - 1, index + radius);
            var sum = 0.0;

            for (var cursor = start; cursor <= end; cursor++)
            {
                sum += values[cursor];
            }

            smoothed[index] = sum / (end - start + 1);
        }

        return smoothed;
    }

    private static Point[] TryGetContourCandidatePoints(Point[] contour, Rect contourBounds, Mat candidateMask)
    {
        using var contourMask = new Mat(contourBounds.Height, contourBounds.Width, MatType.CV_8UC1, Scalar.All(0));
        var contourInRoi = contour
            .Select(point => new Point(point.X - contourBounds.X, point.Y - contourBounds.Y))
            .ToArray();
        Cv2.FillPoly(contourMask, [contourInRoi], Scalar.All(BinaryMaskMaxValue));

        using var candidateRegion = new Mat(candidateMask, contourBounds);
        using var maskedCandidates = new Mat();
        using var candidatePointIndex = new Mat();
        Cv2.BitwiseAnd(candidateRegion, contourMask, maskedCandidates);
        Cv2.FindNonZero(maskedCandidates, candidatePointIndex);
        if (candidatePointIndex.Empty())
        {
            return [];
        }

        candidatePointIndex.GetArray(out Point[] candidatePoints);
        return candidatePoints;
    }

    private static PointClusterEvaluation? TryBuildPointClusterPolygons(Point[] candidatePoints, int clusterCount, Point contourOffset, Size bounds)
    {
        using var samples = new Mat(candidatePoints.Length, 2, MatType.CV_32FC1);
        for (var index = 0; index < candidatePoints.Length; index++)
        {
            samples.Set(index, 0, candidatePoints[index].X);
            samples.Set(index, 1, candidatePoints[index].Y);
        }

        using var labels = new Mat();
        using var centers = new Mat();
        var criteria = new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 20, 1.0);
        Cv2.Kmeans(samples, clusterCount, labels, criteria, PointClusterAttempts, KMeansFlags.PpCenters, centers);
        labels.GetArray(out int[] labelValues);

        var groupedPoints = new List<Point>[clusterCount];
        var centerPoints = new Point2d[clusterCount];
        for (var index = 0; index < clusterCount; index++)
        {
            groupedPoints[index] = [];
            centerPoints[index] = new Point2d(centers.At<float>(index, 0), centers.At<float>(index, 1));
        }

        for (var index = 0; index < candidatePoints.Length; index++)
        {
            groupedPoints[labelValues[index]].Add(candidatePoints[index]);
        }

        var polygons = new List<Point[]>(clusterCount);
        var maxAverageRadius = 0.0;
        for (var index = 0; index < groupedPoints.Length; index++)
        {
            if (groupedPoints[index].Count < MinimumSplitPointCount)
            {
                return null;
            }

            var clusterPoints = groupedPoints[index].ToArray();
            var clusterBounds = Cv2.BoundingRect(clusterPoints);
            if ((clusterBounds.Width * clusterBounds.Height) < MinimumRefinedComponentBoundingArea)
            {
                return null;
            }

            var averageRadius = clusterPoints
                .Average(point => Distance(new Point2d(point.X, point.Y), centerPoints[index]));
            maxAverageRadius = Math.Max(maxAverageRadius, averageRadius);

            var polygon = BuildPolygonFromPoints(clusterPoints, contourOffset, bounds);
            if (polygon.Length < 3)
            {
                return null;
            }

            polygons.Add(polygon);
        }

        var minimumCenterDistance = double.MaxValue;
        for (var firstIndex = 0; firstIndex < centerPoints.Length; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < centerPoints.Length; secondIndex++)
            {
                    minimumCenterDistance = Math.Min(
                        minimumCenterDistance,
                        Distance(centerPoints[firstIndex], centerPoints[secondIndex]));
            }
        }

        if (minimumCenterDistance < PointClusterMinimumCentroidDistance)
        {
            return null;
        }

        var separationRatio = maxAverageRadius <= double.Epsilon
            ? minimumCenterDistance
            : minimumCenterDistance / maxAverageRadius;
        if (separationRatio < PointClusterMinimumSeparationRatio)
        {
            return null;
        }

        return new PointClusterEvaluation(polygons, separationRatio);
    }

    private static List<Point> ReduceSeedCenters(IReadOnlyList<Point> seedCenters)
    {
        var reducedCenters = new List<Point>(seedCenters.Count);

        foreach (var seedCenter in seedCenters)
        {
            if (reducedCenters.Any(existingCenter => Distance(existingCenter, seedCenter) < DensitySeedMinimumCentroidDistance))
            {
                continue;
            }

            reducedCenters.Add(seedCenter);
        }

        return reducedCenters;
    }

    private static Point GetContourCentroid(Point[] contour)
    {
        var moments = Cv2.Moments(contour);
        if (Math.Abs(moments.M00) <= double.Epsilon)
        {
            var bounds = Cv2.BoundingRect(contour);
            return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        }

        return new Point(
            (int)Math.Round(moments.M10 / moments.M00),
            (int)Math.Round(moments.M01 / moments.M00));
    }

    private static double Distance(Point2d firstPoint, Point2d secondPoint)
    {
        var deltaX = firstPoint.X - secondPoint.X;
        var deltaY = firstPoint.Y - secondPoint.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static double Distance(Point firstPoint, Point secondPoint)
    {
        var deltaX = firstPoint.X - secondPoint.X;
        var deltaY = firstPoint.Y - secondPoint.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private readonly record struct PointClusterEvaluation(List<Point[]> Polygons, double Score);

    private static Point[] BuildPolygonFromContour(Point[] contour, Size bounds)
    {
        var contourBounds = Cv2.BoundingRect(contour);
        using var mask = new Mat(contourBounds.Height, contourBounds.Width, MatType.CV_8UC1, Scalar.All(0));
        var contourInBounds = contour
            .Select(point => new Point(point.X - contourBounds.X, point.Y - contourBounds.Y))
            .ToArray();
        Cv2.FillPoly(mask, [contourInBounds], Scalar.All(BinaryMaskMaxValue));
        return BuildPolygonFromMask(mask, contourBounds.Location, bounds);
    }

    private static Point[] BuildPolygonFromPoints(Point[] points, Point offset, Size bounds)
    {
        var translatedPoints = points
            .Select(point => new Point(point.X + offset.X, point.Y + offset.Y))
            .ToArray();

        var pointBounds = Cv2.BoundingRect(translatedPoints);
        pointBounds = ExpandRect(pointBounds, PointCloudMargin, bounds);

        using var pointMask = new Mat(pointBounds.Height, pointBounds.Width, MatType.CV_8UC1, Scalar.All(0));

        foreach (var point in translatedPoints)
        {
            Cv2.Circle(
                pointMask,
                new Point(point.X - pointBounds.X, point.Y - pointBounds.Y),
                PointCloudSeedRadius,
                Scalar.All(BinaryMaskMaxValue),
                -1,
                LineTypes.AntiAlias);
        }

        return BuildPolygonFromMask(pointMask, pointBounds.Location, bounds);
    }

    private static Point[] BuildPolygonFromMask(Mat mask, Point offset, Size bounds)
    {
        if (Cv2.CountNonZero(mask) == 0)
        {
            return Array.Empty<Point>();
        }

        using var paddedMask = new Mat();
        using var closeKernel = Cv2.GetStructuringElement(
            MorphShapes.Ellipse,
            new Size(PolygonMaskCloseKernelSize, PolygonMaskCloseKernelSize));
        Cv2.MorphologyEx(mask, paddedMask, MorphTypes.Close, closeKernel);

        var padding = CalculateMaskPadding(Cv2.CountNonZero(mask));
        var kernelSize = (padding * 2) + 1;
        using var dilateKernel = Cv2.GetStructuringElement(
            MorphShapes.Ellipse,
            new Size(kernelSize, kernelSize));
        Cv2.Dilate(paddedMask, paddedMask, dilateKernel);

        Cv2.FindContours(
            paddedMask,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var expandedContour = contours
            .OrderByDescending(points => Math.Abs(Cv2.ContourArea(points)))
            .FirstOrDefault();
        if (expandedContour is null || expandedContour.Length < 3)
        {
            return Array.Empty<Point>();
        }

        var balloonContour = BalloonizePolygon(expandedContour, bounds);
        var simplified = SimplifyPolygon(balloonContour, MaximumPolygonPoints);
        var translatedPolygon = simplified
            .Select(point => new Point(
                Math.Clamp(point.X + offset.X, 0, bounds.Width - 1),
                Math.Clamp(point.Y + offset.Y, 0, bounds.Height - 1)))
            .ToArray();
        return EnforceMinimumPolygonFootprint(translatedPolygon, bounds);
    }

    private static Point[] TranslatePolygon(Point[] polygon, Rect playfieldBounds)
    {
        return polygon
            .Select(point => new Point(point.X + playfieldBounds.X, point.Y + playfieldBounds.Y))
            .ToArray();
    }

    private static Point[] ClipPolygonToMaximumY(Point[] polygon, int maximumY)
    {
        return ClipPolygonWithHorizontalBoundary(
            polygon,
            point => point.Y <= maximumY,
            (start, end) => IntersectSegmentWithHorizontalBoundary(start, end, maximumY));
    }

    private static Point[] ClipPolygonToMinimumY(Point[] polygon, int minimumY)
    {
        return ClipPolygonWithHorizontalBoundary(
            polygon,
            point => point.Y >= minimumY,
            (start, end) => IntersectSegmentWithHorizontalBoundary(start, end, minimumY));
    }

    private static Point[] ClipPolygonToMaximumX(Point[] polygon, int maximumX)
    {
        return ClipPolygonWithVerticalBoundary(
            polygon,
            point => point.X <= maximumX,
            (start, end) => IntersectSegmentWithVerticalBoundary(start, end, maximumX));
    }

    private static Point[] ClipPolygonToMinimumX(Point[] polygon, int minimumX)
    {
        return ClipPolygonWithVerticalBoundary(
            polygon,
            point => point.X >= minimumX,
            (start, end) => IntersectSegmentWithVerticalBoundary(start, end, minimumX));
    }

    internal static IReadOnlyList<Point[]> ApplyMarkerBoundaryConstraints(IReadOnlyList<Point[]> polygons, IReadOnlyList<Rect> markerBounds)
    {
        if (polygons.Count == 0 || markerBounds.Count < 4)
        {
            return polygons;
        }

        var topMarkers = markerBounds
            .OrderBy(marker => marker.Y + (marker.Height / 2.0))
            .Take(2)
            .ToArray();
        var leftBoundary = markerBounds.Min(marker => marker.X);
        var topBoundary = markerBounds.Min(marker => marker.Y);
        var rightBoundary = markerBounds.Max(marker => marker.Right);
        var bottomBoundary = markerBounds.Max(marker => marker.Bottom);
        var topMarkerCeiling = topMarkers.Min(marker => marker.Y);
        var averageTopMarkerHeight = topMarkers.Average(marker => marker.Height);
        var topBandCentroidThreshold = topMarkerCeiling + (averageTopMarkerHeight * TopMarkerBandPolygonCentroidScale);
        var adjustedPolygons = new List<Point[]>(polygons.Count);

        foreach (var polygon in polygons)
        {
            var clippedPolygon = polygon;
            clippedPolygon = ClipPolygonToMinimumX(clippedPolygon, leftBoundary);
            if (clippedPolygon.Length < 3)
            {
                adjustedPolygons.Add(polygon);
                continue;
            }

            clippedPolygon = ClipPolygonToMaximumX(clippedPolygon, rightBoundary);
            if (clippedPolygon.Length < 3)
            {
                adjustedPolygons.Add(polygon);
                continue;
            }

            var centroid = GetCentroid(clippedPolygon);
            var clippedBounds = Cv2.BoundingRect(clippedPolygon);
            if (clippedBounds.Top < topMarkerCeiling && centroid.Y <= topBandCentroidThreshold)
            {
                clippedPolygon = ClipPolygonToMinimumY(clippedPolygon, topMarkerCeiling);
                if (clippedPolygon.Length < 3)
                {
                    adjustedPolygons.Add(polygon);
                    continue;
                }
            }

            clippedPolygon = ClipPolygonToMinimumY(clippedPolygon, topBoundary);
            if (clippedPolygon.Length < 3)
            {
                adjustedPolygons.Add(polygon);
                continue;
            }

            clippedPolygon = ClipPolygonToMaximumY(clippedPolygon, bottomBoundary);
            adjustedPolygons.Add(clippedPolygon.Length >= 3 ? clippedPolygon : polygon);
        }

        return adjustedPolygons;
    }

    internal static void RandomizePolygons(IList<Point[]> polygons, Random? random = null)
    {
        random ??= Random.Shared;

        for (var polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
        {
            var polygon = polygons[polygonIndex];
            if (polygon.Length < 3)
            {
                continue;
            }

            var randomizedPointCount = Math.Max(
                1,
                (int)Math.Round(polygon.Length * RandomizedPointRatio, MidpointRounding.AwayFromZero));
            randomizedPointCount = Math.Min(randomizedPointCount, polygon.Length);

            var randomizedIndices = Enumerable.Range(0, polygon.Length)
                .OrderBy(_ => random.Next())
                .Take(randomizedPointCount)
                .ToArray();
            var randomizedPolygon = polygon.ToArray();

            foreach (var pointIndex in randomizedIndices)
            {
                var angle = random.NextDouble() * Math.PI * 2.0;
                var distance = random.Next(MinimumRandomizedPointDistance, MaximumRandomizedPointDistance + 1);
                var offsetX = (int)Math.Round(Math.Cos(angle) * distance);
                var offsetY = (int)Math.Round(Math.Sin(angle) * distance);

                if (offsetX == 0 && offsetY == 0)
                {
                    offsetX = distance;
                }

                randomizedPolygon[pointIndex] = new Point(
                    randomizedPolygon[pointIndex].X + offsetX,
                    randomizedPolygon[pointIndex].Y + offsetY);
            }

            polygons[polygonIndex] = randomizedPolygon;
        }
    }

    internal static void FinalizeDetectedPolygons(IList<Point[]> polygons, IReadOnlyList<Rect> markerBounds)
    {
        NormalizePolygons(polygons, mergeCloseNeighboringPoints: false);
        OverwritePolygons(polygons, ApplyMarkerBoundaryConstraints(polygons.ToArray(), markerBounds));
        ResolvePolygonCollisions(polygons);
        EnsureMinimumPointSpacing(polygons);
        NormalizePolygons(polygons, mergeCloseNeighboringPoints: false);
        OverwritePolygons(polygons, ApplyMarkerBoundaryConstraints(polygons.ToArray(), markerBounds));
        ResolvePolygonCollisions(polygons);
        EnsureMinimumPointSpacing(polygons);
        NormalizePolygons(polygons);
        OverwritePolygons(polygons, ApplyMarkerBoundaryConstraints(polygons.ToArray(), markerBounds));
    }

    internal static void NormalizePolygons(IList<Point[]> polygons, bool mergeCloseNeighboringPoints = true)
    {
        for (var polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
        {
            polygons[polygonIndex] = NormalizePolygon(polygons[polygonIndex], mergeCloseNeighboringPoints);
        }
    }

    internal static Point[] NormalizePolygon(Point[] polygon, bool mergeCloseNeighboringPoints = true)
    {
        if (polygon.Length < 3)
        {
            return polygon;
        }

        var normalizedPolygon = Cv2.IsContourConvex(polygon)
            ? polygon
            : Cv2.ConvexHull(polygon);
        return mergeCloseNeighboringPoints
            ? MergeCloseNeighboringPoints(normalizedPolygon)
            : normalizedPolygon;
    }

    private static Point[] MergeCloseNeighboringPoints(Point[] polygon)
    {
        if (polygon.Length <= 3)
        {
            return polygon;
        }

        var mergedPoints = new List<Point> { polygon[0] };

        for (var pointIndex = 1; pointIndex < polygon.Length; pointIndex++)
        {
            var candidate = polygon[pointIndex];
            var remainingPointsAfterCandidate = polygon.Length - pointIndex - 1;
            var canSkipCandidate = mergedPoints.Count + remainingPointsAfterCandidate >= 3;
            if (canSkipCandidate && Distance(candidate, mergedPoints[^1]) < MinimumNeighboringPolygonPointSpacing)
            {
                continue;
            }

            mergedPoints.Add(candidate);
        }

        while (mergedPoints.Count > 3 &&
               Distance(mergedPoints[0], mergedPoints[^1]) < MinimumNeighboringPolygonPointSpacing)
        {
            mergedPoints.RemoveAt(mergedPoints.Count - 1);
        }

        return mergedPoints.ToArray();
    }

    private static void OverwritePolygons(IList<Point[]> target, IReadOnlyList<Point[]> source)
    {
        target.Clear();

        foreach (var polygon in source)
        {
            target.Add(polygon);
        }
    }

    private static Point[] ClipPolygonWithHorizontalBoundary(
        Point[] polygon,
        Func<Point, bool> isInside,
        Func<Point, Point, Point?> intersect)
    {
        if (polygon.Length < 3)
        {
            return [];
        }

        var clipped = new List<Point>();

        for (var index = 0; index < polygon.Length; index++)
        {
            var current = polygon[index];
            var next = polygon[(index + 1) % polygon.Length];
            var currentInside = isInside(current);
            var nextInside = isInside(next);

            if (currentInside && nextInside)
            {
                clipped.Add(next);
                continue;
            }

            if (currentInside && !nextInside)
            {
                var intersection = intersect(current, next);
                if (intersection is not null)
                {
                    clipped.Add(intersection.Value);
                }

                continue;
            }

            if (!currentInside && nextInside)
            {
                var intersection = intersect(current, next);
                if (intersection is not null)
                {
                    clipped.Add(intersection.Value);
                }

                clipped.Add(next);
            }
        }

        var distinctPoints = clipped
            .Distinct()
            .ToArray();

        return distinctPoints.Length <= MaximumPolygonPoints
            ? distinctPoints
            : SimplifyPolygon(distinctPoints, MaximumPolygonPoints);
    }

    private static Point[] ClipPolygonWithVerticalBoundary(
        Point[] polygon,
        Func<Point, bool> isInside,
        Func<Point, Point, Point?> intersect)
    {
        if (polygon.Length < 3)
        {
            return [];
        }

        var clipped = new List<Point>();

        for (var index = 0; index < polygon.Length; index++)
        {
            var current = polygon[index];
            var next = polygon[(index + 1) % polygon.Length];
            var currentInside = isInside(current);
            var nextInside = isInside(next);

            if (currentInside && nextInside)
            {
                clipped.Add(next);
                continue;
            }

            if (currentInside && !nextInside)
            {
                var intersection = intersect(current, next);
                if (intersection is not null)
                {
                    clipped.Add(intersection.Value);
                }

                continue;
            }

            if (!currentInside && nextInside)
            {
                var intersection = intersect(current, next);
                if (intersection is not null)
                {
                    clipped.Add(intersection.Value);
                }

                clipped.Add(next);
            }
        }

        var distinctPoints = clipped
            .Distinct()
            .ToArray();

        return distinctPoints.Length <= MaximumPolygonPoints
            ? distinctPoints
            : SimplifyPolygon(distinctPoints, MaximumPolygonPoints);
    }

    private static Point? IntersectSegmentWithHorizontalBoundary(Point start, Point end, int boundaryY)
    {
        var dy = end.Y - start.Y;
        if (dy == 0)
        {
            return null;
        }

        var t = (boundaryY - start.Y) / (double)dy;
        if (t is < 0.0 or > 1.0)
        {
            return null;
        }

        return new Point(
            (int)Math.Round(start.X + ((end.X - start.X) * t)),
            boundaryY);
    }

    private static Point? IntersectSegmentWithVerticalBoundary(Point start, Point end, int boundaryX)
    {
        var dx = end.X - start.X;
        if (dx == 0)
        {
            return null;
        }

        var t = (boundaryX - start.X) / (double)dx;
        if (t is < 0.0 or > 1.0)
        {
            return null;
        }

        return new Point(
            boundaryX,
            (int)Math.Round(start.Y + ((end.Y - start.Y) * t)));
    }

    private static Point[] SimplifyPolygon(Point[] contour, int maxPoints)
    {
        var perimeter = Cv2.ArcLength(contour, true);
        var epsilon = Math.Max(MinimumSimplificationEpsilon, perimeter * SimplificationEpsilonScale);

        for (var attempt = 0; attempt < MaxSimplificationAttempts; attempt++)
        {
            var simplified = Cv2.ApproxPolyDP(contour, epsilon, true);
            if (simplified.Length <= maxPoints && simplified.Length >= 3)
            {
                return simplified;
            }

            epsilon *= SimplificationGrowthFactor;
        }

        return contour.Take(maxPoints).ToArray();
    }

    internal static Point[] BalloonizePolygon(Point[] polygon, Size bounds)
    {
        if (polygon.Length < 3)
        {
            return polygon;
        }

        var hull = Cv2.ConvexHull(polygon);
        var expandedHull = ExpandBalloonHull(hull);
        var simplifiedHull = expandedHull.Length <= MaximumPolygonPoints
            ? expandedHull
            : SimplifyPolygon(expandedHull, MaximumPolygonPoints);
        return ClampPolygonToBounds(simplifiedHull, bounds);
    }

    internal static Point[] ClampPolygonToBounds(Point[] polygon, Size bounds)
    {
        return polygon
            .Select(point => new Point(
                Math.Clamp(point.X, 0, bounds.Width - 1),
                Math.Clamp(point.Y, 0, bounds.Height - 1)))
            .ToArray();
    }

    private static Point[] ExpandBalloonHull(Point[] hull)
    {
        var centroid = GetCentroid(hull);
        var expansion = Math.Clamp(
            (int)Math.Round(Math.Sqrt(Math.Abs(Cv2.ContourArea(hull))) * BalloonExpansionScale),
            MinimumBalloonExpansion,
            MaximumBalloonExpansion);

        return hull
            .Select(point =>
            {
                var dx = point.X - centroid.X;
                var dy = point.Y - centroid.Y;
                var length = Math.Sqrt((dx * dx) + (dy * dy));
                if (length < double.Epsilon)
                {
                    return point;
                }

                var scale = (length + expansion) / length;
                return new Point(
                    (int)Math.Round(centroid.X + (dx * scale)),
                    (int)Math.Round(centroid.Y + (dy * scale)));
            })
            .ToArray();
    }

    private static int CalculateMaskPadding(int area)
    {
        var scaledPadding = (int)Math.Round(Math.Sqrt(area) * PolygonMaskPaddingScale);
        return Math.Clamp(scaledPadding, MinimumPolygonMaskPadding, MaximumPolygonMaskPadding);
    }

    private static Rect ExpandRect(Rect rect, int margin, Size bounds)
    {
        var left = Math.Max(0, rect.X - margin);
        var top = Math.Max(0, rect.Y - margin);
        var right = Math.Min(bounds.Width, rect.Right + margin);
        var bottom = Math.Min(bounds.Height, rect.Bottom + margin);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    internal static Point[] EnforceMinimumPolygonFootprint(Point[] polygon, Size bounds)
    {
        if (polygon.Length < 3)
        {
            return polygon;
        }

        var polygonBounds = Cv2.BoundingRect(polygon);
        var currentBoundingArea = polygonBounds.Width * polygonBounds.Height;
        if (currentBoundingArea >= MinimumPolygonBoundingArea)
        {
            return polygon;
        }

        var scale = Math.Sqrt(MinimumPolygonBoundingArea / (double)Math.Max(1, currentBoundingArea));

        var centroid = GetCentroid(polygon);
        var expandedPolygon = polygon
            .Select(point =>
            {
                var scaledX = centroid.X + ((point.X - centroid.X) * scale);
                var scaledY = centroid.Y + ((point.Y - centroid.Y) * scale);
                return new Point(
                    Math.Clamp((int)Math.Round(scaledX), 0, bounds.Width - 1),
                    Math.Clamp((int)Math.Round(scaledY), 0, bounds.Height - 1));
            })
            .ToArray();
        return ForceMinimumBoundingArea(expandedPolygon, bounds);
    }

    private static Point[] ForceMinimumBoundingArea(Point[] polygon, Size bounds)
    {
        var adjustedPolygon = polygon.ToArray();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var adjustedBounds = Cv2.BoundingRect(adjustedPolygon);
            var adjustedBoundingArea = adjustedBounds.Width * adjustedBounds.Height;
            if (adjustedBoundingArea >= MinimumPolygonBoundingArea)
            {
                return adjustedPolygon;
            }

            var areaScale = Math.Sqrt(MinimumPolygonBoundingArea / (double)Math.Max(1, adjustedBoundingArea));
            areaScale = Math.Max(areaScale, 1.05);

            var centerX = adjustedBounds.X + ((adjustedBounds.Width - 1) / 2.0);
            var centerY = adjustedBounds.Y + ((adjustedBounds.Height - 1) / 2.0);
            for (var index = 0; index < adjustedPolygon.Length; index++)
            {
                adjustedPolygon[index].X = Math.Clamp(
                    (int)Math.Round(centerX + ((adjustedPolygon[index].X - centerX) * areaScale)),
                    0,
                    bounds.Width - 1);
                adjustedPolygon[index].Y = Math.Clamp(
                    (int)Math.Round(centerY + ((adjustedPolygon[index].Y - centerY) * areaScale)),
                    0,
                    bounds.Height - 1);
            }
        }

        var finalBounds = Cv2.BoundingRect(adjustedPolygon);
        if ((finalBounds.Width * finalBounds.Height) >= MinimumPolygonBoundingArea)
        {
            return adjustedPolygon;
        }

        var width = Math.Max(1, finalBounds.Width);
        var height = Math.Max(1, finalBounds.Height);
        var finalScale = Math.Sqrt(MinimumPolygonBoundingArea / (double)(width * height));
        var fallbackCenterX = finalBounds.X + ((finalBounds.Width - 1) / 2.0);
        var fallbackCenterY = finalBounds.Y + ((finalBounds.Height - 1) / 2.0);
        return adjustedPolygon
            .Select(point => new Point(
                Math.Clamp((int)Math.Round(fallbackCenterX + ((point.X - fallbackCenterX) * finalScale)), 0, bounds.Width - 1),
                Math.Clamp((int)Math.Round(fallbackCenterY + ((point.Y - fallbackCenterY) * finalScale)), 0, bounds.Height - 1)))
            .ToArray();
    }

    internal static void ResolvePolygonCollisions(IList<Point[]> polygons)
    {
        for (var pass = 0; pass < MaximumCollisionResolutionPasses; pass++)
        {
            var changed = false;

            for (var firstIndex = 0; firstIndex < polygons.Count; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < polygons.Count; secondIndex++)
                {
                    if (!PolygonsOverlap(polygons[firstIndex], polygons[secondIndex]))
                    {
                        continue;
                    }

                    if (TrySeparateAxisAlignedPolygons(polygons[firstIndex], polygons[secondIndex], out var firstSeparated, out var secondSeparated))
                    {
                        polygons[firstIndex] = firstSeparated;
                        polygons[secondIndex] = secondSeparated;
                        changed = true;
                        continue;
                    }

                    var firstArea = Math.Abs(Cv2.ContourArea(polygons[firstIndex]));
                    var secondArea = Math.Abs(Cv2.ContourArea(polygons[secondIndex]));
                    var largerIndex = firstArea >= secondArea ? firstIndex : secondIndex;
                    var smallerIndex = largerIndex == firstIndex ? secondIndex : firstIndex;
                    var clipped = ClipPolygonAwayFromOther(polygons[largerIndex], polygons[smallerIndex]);
                    if (clipped.Length < 3)
                    {
                        continue;
                    }

                    polygons[largerIndex] = clipped.Length > MaximumPolygonPoints
                        ? SimplifyPolygon(clipped, MaximumPolygonPoints)
                        : clipped;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    internal static void EnsureMinimumPointSpacing(IList<Point[]> polygons)
    {
        for (var pass = 0; pass < MaximumPointSpacingResolutionPasses; pass++)
        {
            var changed = false;

            for (var firstIndex = 0; firstIndex < polygons.Count; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < polygons.Count; secondIndex++)
                {
                    if (!TryFindClosestPolygonSpacingViolation(
                            polygons[firstIndex],
                            polygons[secondIndex],
                            out var violation))
                    {
                        continue;
                    }

                    if (violation.Distance >= MinimumInterPolygonPointSpacing)
                    {
                        continue;
                    }

                    var firstPoints = polygons[firstIndex].ToArray();
                    var secondPoints = polygons[secondIndex].ToArray();
                    var firstPoint = firstPoints[violation.FirstPointIndex];
                    var secondPoint = secondPoints[violation.SecondPointIndex];
                    var dx = violation.FirstReferencePoint.X - violation.SecondReferencePoint.X;
                    var dy = violation.FirstReferencePoint.Y - violation.SecondReferencePoint.Y;

                    if (dx == 0 && dy == 0)
                    {
                        var firstCentroid = GetCentroid(firstPoints);
                        var secondCentroid = GetCentroid(secondPoints);
                        dx = firstCentroid.X >= secondCentroid.X ? 1 : -1;
                        dy = firstCentroid.Y >= secondCentroid.Y ? 1 : -1;
                    }

                    var length = Math.Sqrt((dx * dx) + (dy * dy));
                    var missingDistance = MinimumInterPolygonPointSpacing - violation.Distance;
                    var offsetScale = (missingDistance / 2.0) / length;
                    var offsetX = (int)Math.Ceiling(Math.Abs(dx * offsetScale)) * Math.Sign(dx);
                    var offsetY = (int)Math.Ceiling(Math.Abs(dy * offsetScale)) * Math.Sign(dy);

                    if (offsetX == 0 && offsetY == 0)
                    {
                        offsetX = Math.Sign(dx);
                        offsetY = Math.Sign(dy);
                    }

                    firstPoints[violation.FirstPointIndex] = new Point(firstPoint.X + offsetX, firstPoint.Y + offsetY);
                    secondPoints[violation.SecondPointIndex] = new Point(secondPoint.X - offsetX, secondPoint.Y - offsetY);
                    polygons[firstIndex] = firstPoints;
                    polygons[secondIndex] = secondPoints;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private static bool TryFindClosestPolygonSpacingViolation(
        IReadOnlyList<Point> firstPolygon,
        IReadOnlyList<Point> secondPolygon,
        out PolygonSpacingViolation violation)
    {
        violation = default;
        var foundViolation = false;
        var minimumDistance = double.MaxValue;

        for (var firstIndex = 0; firstIndex < firstPolygon.Count; firstIndex++)
        {
            for (var secondIndex = 0; secondIndex < secondPolygon.Count; secondIndex++)
            {
                var dx = firstPolygon[firstIndex].X - secondPolygon[secondIndex].X;
                var dy = firstPolygon[firstIndex].Y - secondPolygon[secondIndex].Y;
                var currentDistance = Math.Sqrt((dx * dx) + (dy * dy));
                if (currentDistance < minimumDistance)
                {
                    minimumDistance = currentDistance;
                    violation = new PolygonSpacingViolation(
                        firstIndex,
                        secondIndex,
                        new Point2d(firstPolygon[firstIndex].X, firstPolygon[firstIndex].Y),
                        new Point2d(secondPolygon[secondIndex].X, secondPolygon[secondIndex].Y),
                        currentDistance);
                    foundViolation = true;
                }
            }
        }

        for (var firstIndex = 0; firstIndex < firstPolygon.Count; firstIndex++)
        {
            for (var secondIndex = 0; secondIndex < secondPolygon.Count; secondIndex++)
            {
                var segmentStart = secondPolygon[secondIndex];
                var segmentEnd = secondPolygon[(secondIndex + 1) % secondPolygon.Count];
                var closestPoint = FindClosestPointOnSegment(firstPolygon[firstIndex], segmentStart, segmentEnd);
                var currentDistance = Distance(firstPolygon[firstIndex], closestPoint);
                if (currentDistance < minimumDistance)
                {
                    minimumDistance = currentDistance;
                    violation = new PolygonSpacingViolation(
                        firstIndex,
                        secondIndex,
                        new Point2d(firstPolygon[firstIndex].X, firstPolygon[firstIndex].Y),
                        closestPoint,
                        currentDistance);
                    foundViolation = true;
                }
            }
        }

        for (var secondIndex = 0; secondIndex < secondPolygon.Count; secondIndex++)
        {
            for (var firstIndex = 0; firstIndex < firstPolygon.Count; firstIndex++)
            {
                var segmentStart = firstPolygon[firstIndex];
                var segmentEnd = firstPolygon[(firstIndex + 1) % firstPolygon.Count];
                var closestPoint = FindClosestPointOnSegment(secondPolygon[secondIndex], segmentStart, segmentEnd);
                var currentDistance = Distance(secondPolygon[secondIndex], closestPoint);
                if (currentDistance < minimumDistance)
                {
                    minimumDistance = currentDistance;
                    violation = new PolygonSpacingViolation(
                        firstIndex,
                        secondIndex,
                        closestPoint,
                        new Point2d(secondPolygon[secondIndex].X, secondPolygon[secondIndex].Y),
                        currentDistance);
                    foundViolation = true;
                }
            }
        }

        return foundViolation;
    }

    private static Point2d FindClosestPointOnSegment(Point point, Point segmentStart, Point segmentEnd)
    {
        var dx = segmentEnd.X - segmentStart.X;
        var dy = segmentEnd.Y - segmentStart.Y;
        if (dx == 0 && dy == 0)
        {
            return new Point2d(segmentStart.X, segmentStart.Y);
        }

        var tNumerator = ((point.X - segmentStart.X) * dx) + ((point.Y - segmentStart.Y) * dy);
        var tDenominator = (dx * dx) + (dy * dy);
        var t = Math.Clamp(tNumerator / (double)tDenominator, 0.0, 1.0);
        return new Point2d(
            segmentStart.X + (dx * t),
            segmentStart.Y + (dy * t));
    }

    private static double Distance(Point point, Point2d other)
    {
        var dx = point.X - other.X;
        var dy = point.Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private readonly record struct PolygonSpacingViolation(
        int FirstPointIndex,
        int SecondPointIndex,
        Point2d FirstReferencePoint,
        Point2d SecondReferencePoint,
        double Distance);

    private static bool TrySeparateAxisAlignedPolygons(
        Point[] firstPolygon,
        Point[] secondPolygon,
        out Point[] firstSeparated,
        out Point[] secondSeparated)
    {
        firstSeparated = firstPolygon;
        secondSeparated = secondPolygon;

        var firstCentroid = GetCentroid(firstPolygon);
        var secondCentroid = GetCentroid(secondPolygon);
        var deltaX = Math.Abs(firstCentroid.X - secondCentroid.X);
        var deltaY = Math.Abs(firstCentroid.Y - secondCentroid.Y);

        if (deltaY >= deltaX)
        {
            var boundaryY = (int)Math.Round((firstCentroid.Y + secondCentroid.Y) / 2.0);
            var firstIsTop = firstCentroid.Y <= secondCentroid.Y;
            var topPolygon = firstIsTop ? firstPolygon : secondPolygon;
            var bottomPolygon = firstIsTop ? secondPolygon : firstPolygon;
            var separatedTop = ClipPolygonToMaximumY(topPolygon, boundaryY - SplitPolygonSeparationPixels);
            var separatedBottom = ClipPolygonToMinimumY(bottomPolygon, boundaryY + SplitPolygonSeparationPixels);

            if (separatedTop.Length < 3 || separatedBottom.Length < 3)
            {
                return false;
            }

            if (firstIsTop)
            {
                firstSeparated = separatedTop;
                secondSeparated = separatedBottom;
            }
            else
            {
                firstSeparated = separatedBottom;
                secondSeparated = separatedTop;
            }

            return !PolygonsOverlap(firstSeparated, secondSeparated);
        }

        var boundaryX = (int)Math.Round((firstCentroid.X + secondCentroid.X) / 2.0);
        var firstIsLeft = firstCentroid.X <= secondCentroid.X;
        var leftPolygon = firstIsLeft ? firstPolygon : secondPolygon;
        var rightPolygon = firstIsLeft ? secondPolygon : firstPolygon;
        var separatedLeft = ClipPolygonToMaximumX(leftPolygon, boundaryX - SplitPolygonSeparationPixels);
        var separatedRight = ClipPolygonToMinimumX(rightPolygon, boundaryX + SplitPolygonSeparationPixels);

        if (separatedLeft.Length < 3 || separatedRight.Length < 3)
        {
            return false;
        }

        if (firstIsLeft)
        {
            firstSeparated = separatedLeft;
            secondSeparated = separatedRight;
        }
        else
        {
            firstSeparated = separatedRight;
            secondSeparated = separatedLeft;
        }

        return !PolygonsOverlap(firstSeparated, secondSeparated);
    }

    private static bool PolygonsOverlap(Point[] firstPolygon, Point[] secondPolygon)
    {
        var firstBounds = Cv2.BoundingRect(firstPolygon);
        var secondBounds = Cv2.BoundingRect(secondPolygon);
        var overlapBounds = new Rect(
            Math.Max(firstBounds.X, secondBounds.X),
            Math.Max(firstBounds.Y, secondBounds.Y),
            Math.Min(firstBounds.Right, secondBounds.Right) - Math.Max(firstBounds.X, secondBounds.X),
            Math.Min(firstBounds.Bottom, secondBounds.Bottom) - Math.Max(firstBounds.Y, secondBounds.Y));

        if (overlapBounds.Width <= 0 || overlapBounds.Height <= 0)
        {
            return false;
        }

        using var firstMask = new Mat(overlapBounds.Height, overlapBounds.Width, MatType.CV_8UC1, Scalar.All(0));
        using var secondMask = new Mat(overlapBounds.Height, overlapBounds.Width, MatType.CV_8UC1, Scalar.All(0));
        using var overlapMask = new Mat();

        var translatedFirstPolygon = firstPolygon
            .Select(point => new Point(point.X - overlapBounds.X, point.Y - overlapBounds.Y))
            .ToArray();
        var translatedSecondPolygon = secondPolygon
            .Select(point => new Point(point.X - overlapBounds.X, point.Y - overlapBounds.Y))
            .ToArray();

        Cv2.FillPoly(firstMask, [translatedFirstPolygon], Scalar.All(BinaryMaskMaxValue));
        Cv2.FillPoly(secondMask, [translatedSecondPolygon], Scalar.All(BinaryMaskMaxValue));
        Cv2.BitwiseAnd(firstMask, secondMask, overlapMask);

        return Cv2.CountNonZero(overlapMask) > MinimumOverlapArea;
    }

    private static Point[] ClipPolygonAwayFromOther(Point[] polygon, Point[] otherPolygon)
    {
        var polygonCentroid = GetCentroid(polygon);
        var otherCentroid = GetCentroid(otherPolygon);
        var midpoint = new Point2d(
            (polygonCentroid.X + otherCentroid.X) / 2.0,
            (polygonCentroid.Y + otherCentroid.Y) / 2.0);
        var normal = new Point2d(
            polygonCentroid.X - otherCentroid.X,
            polygonCentroid.Y - otherCentroid.Y);

        var clipped = new List<Point>();

        for (var index = 0; index < polygon.Length; index++)
        {
            var current = polygon[index];
            var next = polygon[(index + 1) % polygon.Length];
            var currentInside = IsInsideHalfPlane(current, midpoint, normal);
            var nextInside = IsInsideHalfPlane(next, midpoint, normal);

            if (currentInside && nextInside)
            {
                clipped.Add(next);
                continue;
            }

            if (currentInside && !nextInside)
            {
                var intersection = IntersectSegmentWithHalfPlane(current, next, midpoint, normal);
                if (intersection is not null)
                {
                    clipped.Add(intersection.Value);
                }

                continue;
            }

            if (!currentInside && nextInside)
            {
                var intersection = IntersectSegmentWithHalfPlane(current, next, midpoint, normal);
                if (intersection is not null)
                {
                    clipped.Add(intersection.Value);
                }

                clipped.Add(next);
            }
        }

        return clipped
            .Distinct()
            .ToArray();
    }

    private static Point2d GetCentroid(Point[] polygon)
    {
        return new Point2d(
            polygon.Average(point => point.X),
            polygon.Average(point => point.Y));
    }

    private static bool IsInsideHalfPlane(Point point, Point2d midpoint, Point2d normal)
    {
        var dot = ((point.X - midpoint.X) * normal.X) + ((point.Y - midpoint.Y) * normal.Y);
        return dot >= 0.0;
    }

    private static Point? IntersectSegmentWithHalfPlane(Point start, Point end, Point2d midpoint, Point2d normal)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var denominator = (dx * normal.X) + (dy * normal.Y);
        if (Math.Abs(denominator) < double.Epsilon)
        {
            return null;
        }

        var t = -(((start.X - midpoint.X) * normal.X) + ((start.Y - midpoint.Y) * normal.Y)) / denominator;
        if (t < 0.0 || t > 1.0)
        {
            return null;
        }

        return new Point(
            (int)Math.Round(start.X + (dx * t)),
            (int)Math.Round(start.Y + (dy * t)));
    }

    private static IReadOnlyList<Point[]> BuildFallbackPolygons(Rect playfield)
    {
        var x = playfield.X;
        var y = playfield.Y;
        var w = playfield.Width;
        var h = playfield.Height;

        var topPolygon = new[]
        {
            P(0.14, 0.03),
            P(0.52, 0.01),
            P(0.87, 0.04),
            P(0.95, 0.18),
            P(0.94, 0.33),
            P(0.79, 0.42),
            P(0.56, 0.45),
            P(0.29, 0.45),
            P(0.09, 0.43),
            P(0.00, 0.20)
        };

        var bottomPolygon = new[]
        {
            P(0.03, 0.50),
            P(0.50, 0.50),
            P(0.86, 0.51),
            P(0.96, 0.66),
            P(0.97, 0.86),
            P(0.86, 0.95),
            P(0.53, 0.95),
            P(0.26, 0.94),
            P(0.05, 0.92),
            P(0.00, 0.62)
        };

        return [topPolygon, bottomPolygon];

        Point P(double px, double py) => new(
            x + (int)Math.Round(w * px),
            y + (int)Math.Round(h * py));
    }

    private static Rect BuildFallbackPlayfieldBounds(Size imageSize)
    {
        var left = (int)Math.Round(imageSize.Width * FallbackPlayfieldLeftRatio);
        var top = (int)Math.Round(imageSize.Height * FallbackPlayfieldTopRatio);
        var width = (int)Math.Round(imageSize.Width * FallbackPlayfieldWidthRatio);
        var height = (int)Math.Round(imageSize.Height * FallbackPlayfieldHeightRatio);

        left = Math.Clamp(left, 0, Math.Max(0, imageSize.Width - 1));
        top = Math.Clamp(top, 0, Math.Max(0, imageSize.Height - 1));
        width = Math.Clamp(width, 1, Math.Max(1, imageSize.Width - left));
        height = Math.Clamp(height, 1, Math.Max(1, imageSize.Height - top));

        return new Rect(left, top, width, height);
    }

    private static void DrawDebugOverlay(Mat annotated, PlayfieldDetectionResult playfieldDetection, IReadOnlyList<Point[]> polygons)
    {
        // Draw the recovered playfield and cluster outlines back onto the original
        // screenshot so each output image explains what the detector decided.
        if (playfieldDetection.IsFound)
        {
            Cv2.Rectangle(annotated, playfieldDetection.Bounds, new Scalar(70, 150, 255), OverlayStrokeThickness);

            foreach (var marker in playfieldDetection.MarkerBounds)
            {
                Cv2.Rectangle(annotated, marker, new Scalar(255, 120, 80), OverlayStrokeThickness);
            }
        }

        for (var index = 0; index < polygons.Count; index++)
        {
            var color = Palette[index % Palette.Length];
            Cv2.Polylines(annotated, [polygons[index]], true, color, OverlayStrokeThickness, LineTypes.AntiAlias);

            foreach (var point in polygons[index])
            {
                Cv2.Circle(annotated, point, OverlayPointRadius, color, -1, LineTypes.AntiAlias);
            }
        }

        Cv2.PutText(
            annotated,
            playfieldDetection.IsFound
                ? $"Playfield found, clusters: {polygons.Count}"
                : polygons.Count > 0
                    ? $"Playfield not found, using fallback: {polygons.Count}"
                    : "Playfield not found",
            new Point(
                playfieldDetection.IsFound ? playfieldDetection.Bounds.X : OverlayLeftPadding,
                playfieldDetection.IsFound ? Math.Max(OverlayMinimumLabelY, playfieldDetection.Bounds.Y - OverlayLabelYOffset) : OverlayTopPadding),
            HersheyFonts.HersheySimplex,
            OverlayTextScale,
            playfieldDetection.IsFound ? new Scalar(80, 220, 120) : new Scalar(80, 120, 255),
            OverlayTextThickness,
            LineTypes.AntiAlias);
    }
}

internal sealed record SampleProcessingSummary(
    string SamplesDirectory,
    IReadOnlyList<SampleProcessingResult> Results);

internal sealed record SampleProcessingResult(
    string FileName,
    bool PlayfieldFound,
    int ClusterCount,
    string OutputPath);

internal sealed record SampleImageAnalysisResult(
    SampleProcessingResult Result,
    PlayfieldDetectionResult PlayfieldDetection,
    IReadOnlyList<Point[]> Polygons);
