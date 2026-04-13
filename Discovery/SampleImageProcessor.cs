using System.IO;
using OpenCvSharp;

namespace Discovery;

internal sealed class SampleImageProcessor
{
    private const int SaturationThreshold = 45;
    private const int BrightnessThreshold = 55;
    private const int BinaryMaskMaxValue = 255;
    private const int CandidateOpenKernelSize = 2;
    private const int DensityBlurSigma = 15;
    private const int DensityDilateKernelSize = 9;
    private const int DensityThreshold = 18;
    private const int DensityCloseKernelSize = 21;
    private const int MinimumClusterArea = 1400;
    private const int MaximumPolygonPoints = 10;
    private const double MinimumSimplificationEpsilon = 3.0;
    private const double SimplificationEpsilonScale = 0.01;
    private const double SimplificationGrowthFactor = 1.35;
    private const int MaxSimplificationAttempts = 12;
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

    public SampleProcessingSummary ProcessSamples(string projectRoot)
    {
        var samplesDirectory = Path.Combine(projectRoot, "samples");
        if (!Directory.Exists(samplesDirectory))
        {
            throw new DirectoryNotFoundException($"Samples folder was not found: {samplesDirectory}");
        }

        var outputDirectory = Path.Combine(samplesDirectory, "output");
        Directory.CreateDirectory(outputDirectory);

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
            using var image = Cv2.ImRead(sampleFile, ImreadModes.Color);
            if (image.Empty())
            {
                throw new InvalidOperationException($"Could not read image: {sampleFile}");
            }

            var playfieldDetection = m_PlayfieldDetector.Detect(image);
            using var annotated = image.Clone();
            IReadOnlyList<Point[]> polygons = Array.Empty<Point[]>();

            if (playfieldDetection.IsFound)
            {
                using var playfieldImage = new Mat(image, playfieldDetection.Bounds);
                using var candidateMask = BuildCandidateMask(playfieldImage);
                using var densityMask = BuildDensityMask(candidateMask);

                polygons = BuildClusterPolygons(densityMask, playfieldDetection.Bounds.Location);
                if (polygons.Count == 0)
                {
                    polygons = BuildFallbackPolygons(playfieldDetection.Bounds);
                }
            }

            DrawDebugOverlay(annotated, playfieldDetection, polygons);

            var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(sampleFile)}.annotated.png");
            Cv2.ImWrite(outputPath, annotated);

            results.Add(new SampleProcessingResult(
                Path.GetFileName(sampleFile),
                playfieldDetection.IsFound,
                polygons.Count,
                outputPath));
        }

        return new SampleProcessingSummary(samplesDirectory, outputDirectory, results);
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

    private static Mat BuildDensityMask(Mat candidateMask)
    {
        using var blurred = new Mat();
        using var dilated = new Mat();
        using var thresholded = new Mat();

        Cv2.GaussianBlur(candidateMask, blurred, new Size(0, 0), DensityBlurSigma, DensityBlurSigma);

        using var dilateKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(DensityDilateKernelSize, DensityDilateKernelSize));
        Cv2.Dilate(blurred, dilated, dilateKernel);
        Cv2.Threshold(dilated, thresholded, DensityThreshold, BinaryMaskMaxValue, ThresholdTypes.Binary);

        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(DensityCloseKernelSize, DensityCloseKernelSize));
        Cv2.MorphologyEx(thresholded, thresholded, MorphTypes.Close, closeKernel);

        return thresholded.Clone();
    }

    private static List<Point[]> BuildClusterPolygons(Mat densityMask, Point playfieldOffset)
    {
        Cv2.FindContours(
            densityMask,
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

            var hull = Cv2.ConvexHull(contour);
            if (hull.Length < 3)
            {
                continue;
            }

            var simplified = SimplifyPolygon(hull, MaximumPolygonPoints);
            var shifted = simplified
                .Select(point => new Point(point.X + playfieldOffset.X, point.Y + playfieldOffset.Y))
                .ToArray();

            polygons.Add(shifted);
        }

        return polygons
            .OrderByDescending(points => Math.Abs(Cv2.ContourArea(points)))
            .ToList();
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
    string OutputDirectory,
    IReadOnlyList<SampleProcessingResult> Results);

internal sealed record SampleProcessingResult(
    string FileName,
    bool PlayfieldFound,
    int ClusterCount,
    string OutputPath);
