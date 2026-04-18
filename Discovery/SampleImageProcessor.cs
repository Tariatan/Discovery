using System.IO;
using OpenCvSharp;

namespace Discovery;

internal sealed class SampleImageProcessor
{
    private const string SamplesFolderName = "samples";
    private const int SaturationThreshold = 45;
    private const int BrightnessThreshold = 55;
    private const int BinaryMaskMaxValue = 255;
    private const int CandidateOpenKernelSize = 2;
    private const int ClusterBlurSigma = 20;
    private const int ClusterDilateKernelSize = 15;
    private const int ClusterThreshold = 10;
    private const int ClusterCloseKernelSize = 31;
    private const int ClusterOpenKernelSize = 5;
    private const int MinimumClusterArea = 1400;
    private const int MinimumSplitSegmentHeight = 70;
    private const int MinimumSplitPointCount = 180;
    private const double MinimumSplitAspectRatio = 1.10;
    private const int HistogramSmoothingRadius = 6;
    private const double MaximumSplitValleyRatio = 0.72;
    private const int MinimumSplitPeakDensity = 10;
    private const int SplitPolygonSeparationPixels = 2;
    private const double MinimumInterPolygonPointSpacing = 15.0;
    private const int MaximumPointSpacingResolutionPasses = 15;
    private const int MaximumPolygonsPerSession = 8;
    private const int MaximumPolygonPoints = 10;
    private const double MinimumSimplificationEpsilon = 3.0;
    private const double SimplificationEpsilonScale = 0.01;
    private const double SimplificationGrowthFactor = 1.35;
    private const int MaxSimplificationAttempts = 12;
    private const double PolygonPaddingScale = 0.18;
    private const int MinimumPolygonPadding = 10;
    private const double MinimumOverlapArea = 1.0;
    private const int MaximumCollisionResolutionPasses = 6;
    private const int OverlayStrokeThickness = 2;
    private const int OverlayPointRadius = 4;
    private const double OverlayTextScale = 0.8;
    private const int OverlayTextThickness = 2;
    private const int OverlayLeftPadding = 30;
    private const int OverlayTopPadding = 40;
    private const int OverlayLabelYOffset = 14;
    private const int OverlayMinimumLabelY = 30;

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

        if (playfieldDetection.IsFound)
        {
            using var playfieldImage = new Mat(image, playfieldDetection.Bounds);
            using var candidateMask = BuildCandidateMask(playfieldImage);
            using var clusterMask = BuildClusterMask(candidateMask);

            polygons = BuildClusterPolygons(candidateMask, clusterMask, playfieldDetection.Bounds);
            if (polygons.Count == 0)
            {
                polygons = BuildFallbackPolygons(playfieldDetection.Bounds);
            }
        }

        DrawDebugOverlay(annotated, playfieldDetection, polygons);

        var outputPath = Path.Combine(Path.GetDirectoryName(imagePath)!, Path.GetFileNameWithoutExtension(imagePath) + ".annotated.png");
        Cv2.ImWrite(outputPath, annotated);

        var result = new SampleProcessingResult(
            Path.GetFileName(imagePath),
            playfieldDetection.IsFound,
            polygons.Count,
            outputPath);

        return new SampleImageAnalysisResult(result, playfieldDetection, polygons);
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

    private static List<Point[]> BuildClusterPolygons(Mat candidateMask, Mat clusterMask, Rect playfieldBounds)
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

            var localPolygons = TrySplitContourIntoVerticalSegments(contour, candidateMask, clusterMask.Size());
            if (localPolygons.Count == 0)
            {
                var polygon = BuildPolygonFromContour(contour, clusterMask.Size());
                if (polygon.Length >= 3)
                {
                    polygons.Add(TranslatePolygon(polygon, playfieldBounds));
                }

                continue;
            }

            foreach (var localPolygon in localPolygons)
            {
                polygons.Add(TranslatePolygon(localPolygon, playfieldBounds));
            }
        }

        polygons = polygons
            .OrderByDescending(points => Math.Abs(Cv2.ContourArea(points)))
            .ToList();

        ResolvePolygonCollisions(polygons);
        EnsureMinimumPointSpacing(polygons);
        return polygons.Take(MaximumPolygonsPerSession).ToList();
    }

    private static IReadOnlyList<Point[]> TrySplitContourIntoVerticalSegments(Point[] contour, Mat candidateMask, Size bounds)
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
        using var maskedCandidates = new Mat();
        using var candidatePointIndex = new Mat();
        Cv2.BitwiseAnd(candidateRegion, contourMask, maskedCandidates);
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

        var splitRow = TryFindVerticalSplitRow(candidatePoints, contourBounds.Height);
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

    private static Point[] BuildPolygonFromContour(Point[] contour, Size bounds)
    {
        var hull = Cv2.ConvexHull(contour);
        return BuildPolygonFromHull(hull, bounds);
    }

    private static Point[] BuildPolygonFromPoints(Point[] points, Point offset, Size bounds)
    {
        var translatedPoints = points
            .Select(point => new Point(point.X + offset.X, point.Y + offset.Y))
            .ToArray();
        var hull = Cv2.ConvexHull(translatedPoints);
        return BuildPolygonFromHull(hull, bounds);
    }

    private static Point[] BuildPolygonFromHull(Point[] hull, Size bounds)
    {
        if (hull.Length < 3)
        {
            return Array.Empty<Point>();
        }

        var simplified = SimplifyPolygon(hull, MaximumPolygonPoints);
        var padding = CalculatePolygonPadding(simplified);
        return ExpandPolygon(simplified, padding, bounds);
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

    private static Point[] ClipPolygonWithHorizontalBoundary(
        Point[] polygon,
        Func<Point, bool> isInside,
        Func<Point, Point, Point?> intersect)
    {
        if (polygon.Length < 3)
        {
            return Array.Empty<Point>();
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
            return Array.Empty<Point>();
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

    private static int CalculatePolygonPadding(Point[] polygon)
    {
        var area = Math.Abs(Cv2.ContourArea(polygon));
        return Math.Max(MinimumPolygonPadding, (int)Math.Round(Math.Sqrt(area) * PolygonPaddingScale));
    }

    private static Point[] ExpandPolygon(Point[] polygon, int padding, Size bounds)
    {
        var centroidX = polygon.Average(point => point.X);
        var centroidY = polygon.Average(point => point.Y);

        return polygon.Select(point =>
            {
                var dx = point.X - centroidX;
                var dy = point.Y - centroidY;
                var length = Math.Sqrt((dx * dx) + (dy * dy));
                if (length < double.Epsilon)
                {
                    return point;
                }

                var scale = (length + padding) / length;
                var expandedX = (int)Math.Round(centroidX + (dx * scale));
                var expandedY = (int)Math.Round(centroidY + (dy * scale));
                return new Point(
                    Math.Clamp(expandedX, 0, bounds.Width - 1),
                    Math.Clamp(expandedY, 0, bounds.Height - 1));
            })
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
                    if (!TryFindClosestPointPair(polygons[firstIndex], polygons[secondIndex], out var firstPointIndex, out var secondPointIndex, out var distance))
                    {
                        continue;
                    }

                    if (distance >= MinimumInterPolygonPointSpacing)
                    {
                        continue;
                    }

                    var firstPoints = polygons[firstIndex].ToArray();
                    var secondPoints = polygons[secondIndex].ToArray();
                    var firstPoint = firstPoints[firstPointIndex];
                    var secondPoint = secondPoints[secondPointIndex];
                    var dx = firstPoint.X - secondPoint.X;
                    var dy = firstPoint.Y - secondPoint.Y;

                    if (dx == 0 && dy == 0)
                    {
                        var firstCentroid = GetCentroid(firstPoints);
                        var secondCentroid = GetCentroid(secondPoints);
                        dx = firstCentroid.X >= secondCentroid.X ? 1 : -1;
                        dy = firstCentroid.Y >= secondCentroid.Y ? 1 : -1;
                    }

                    var length = Math.Sqrt((dx * dx) + (dy * dy));
                    var missingDistance = MinimumInterPolygonPointSpacing - distance;
                    var offsetScale = (missingDistance / 2.0) / length;
                    var offsetX = (int)Math.Ceiling(Math.Abs(dx * offsetScale)) * Math.Sign(dx);
                    var offsetY = (int)Math.Ceiling(Math.Abs(dy * offsetScale)) * Math.Sign(dy);

                    if (offsetX == 0 && offsetY == 0)
                    {
                        offsetX = Math.Sign(dx);
                        offsetY = Math.Sign(dy);
                    }

                    firstPoints[firstPointIndex] = new Point(firstPoint.X + offsetX, firstPoint.Y + offsetY);
                    secondPoints[secondPointIndex] = new Point(secondPoint.X - offsetX, secondPoint.Y - offsetY);
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

    private static bool TryFindClosestPointPair(
        IReadOnlyList<Point> firstPolygon,
        IReadOnlyList<Point> secondPolygon,
        out int firstPointIndex,
        out int secondPointIndex,
        out double distance)
    {
        firstPointIndex = -1;
        secondPointIndex = -1;
        distance = double.MaxValue;

        for (var firstIndex = 0; firstIndex < firstPolygon.Count; firstIndex++)
        {
            for (var secondIndex = 0; secondIndex < secondPolygon.Count; secondIndex++)
            {
                var dx = firstPolygon[firstIndex].X - secondPolygon[secondIndex].X;
                var dy = firstPolygon[firstIndex].Y - secondPolygon[secondIndex].Y;
                var currentDistance = Math.Sqrt((dx * dx) + (dy * dy));
                if (currentDistance >= distance)
                {
                    continue;
                }

                distance = currentDistance;
                firstPointIndex = firstIndex;
                secondPointIndex = secondIndex;
            }
        }

        return firstPointIndex >= 0 && secondPointIndex >= 0;
    }

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
        using var firstInput = InputArray.Create(firstPolygon);
        using var secondInput = InputArray.Create(secondPolygon);
        using var overlapPolygon = new Mat();
        var overlapArea = Cv2.IntersectConvexConvex(firstInput, secondInput, overlapPolygon, true);
        return overlapArea > MinimumOverlapArea;
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
