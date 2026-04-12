using System.IO;
using OpenCvSharp;

namespace Discovery;

internal sealed class SampleImageProcessor
{
    private static readonly Scalar[] Palette =
    [
        new Scalar(0, 255, 255),
        new Scalar(255, 180, 0),
        new Scalar(0, 220, 120),
        new Scalar(220, 120, 255),
        new Scalar(80, 180, 255),
        new Scalar(255, 120, 120)
    ];

    private readonly PlayfieldDetector _playfieldDetector = new();

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

        foreach (var sampleFile in sampleFiles)
        {
            using var image = Cv2.ImRead(sampleFile, ImreadModes.Color);
            if (image.Empty())
            {
                throw new InvalidOperationException($"Could not read image: {sampleFile}");
            }

            var playfieldDetection = _playfieldDetector.Detect(image);
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

            Cv2.Threshold(channels[1], saturationMask, 45, 255, ThresholdTypes.Binary);
            Cv2.Threshold(channels[2], brightnessMask, 55, 255, ThresholdTypes.Binary);
            Cv2.BitwiseAnd(saturationMask, brightnessMask, combinedMask);

            using var openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
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

        Cv2.GaussianBlur(candidateMask, blurred, new Size(0, 0), 15, 15);

        using var dilateKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(9, 9));
        Cv2.Dilate(blurred, dilated, dilateKernel);
        Cv2.Threshold(dilated, thresholded, 18, 255, ThresholdTypes.Binary);

        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(21, 21));
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
            if (area < 1400)
            {
                continue;
            }

            var hull = Cv2.ConvexHull(contour);
            if (hull.Length < 3)
            {
                continue;
            }

            var simplified = SimplifyPolygon(hull, 10);
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
        var epsilon = Math.Max(3.0, perimeter * 0.01);

        for (var attempt = 0; attempt < 12; attempt++)
        {
            var simplified = Cv2.ApproxPolyDP(contour, epsilon, true);
            if (simplified.Length <= maxPoints && simplified.Length >= 3)
            {
                return simplified;
            }

            epsilon *= 1.35;
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
        if (playfieldDetection.IsFound)
        {
            Cv2.Rectangle(annotated, playfieldDetection.Bounds, new Scalar(70, 150, 255), 2);

            foreach (var marker in playfieldDetection.MarkerBounds)
            {
                Cv2.Rectangle(annotated, marker, new Scalar(255, 120, 80), 2);
            }
        }

        for (var index = 0; index < polygons.Count; index++)
        {
            var color = Palette[index % Palette.Length];
            Cv2.Polylines(annotated, [polygons[index]], true, color, 2, LineTypes.AntiAlias);

            foreach (var point in polygons[index])
            {
                Cv2.Circle(annotated, point, 4, color, -1, LineTypes.AntiAlias);
            }
        }

        Cv2.PutText(
            annotated,
            playfieldDetection.IsFound
                ? $"Playfield found, clusters: {polygons.Count}"
                : "Playfield not found",
            new Point(
                playfieldDetection.IsFound ? playfieldDetection.Bounds.X : 30,
                playfieldDetection.IsFound ? Math.Max(30, playfieldDetection.Bounds.Y - 14) : 40),
            HersheyFonts.HersheySimplex,
            0.8,
            playfieldDetection.IsFound ? new Scalar(80, 220, 120) : new Scalar(80, 120, 255),
            2,
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
