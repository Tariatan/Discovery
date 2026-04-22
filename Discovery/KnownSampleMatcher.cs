using System.Collections.Concurrent;
using System.IO;
using OpenCvSharp;

namespace Discovery;

internal sealed class KnownSampleMatcher(PlayfieldDetector playfieldDetector)
{
    private const string SamplesFolderName = "expected";
    private const string MaskedExpectedSuffix = ".expected.masked.png";
    private const int SignatureWidth = 96;
    private const int SignatureHeight = 96;
    private const double MaximumMatchScore = 4.0;
    private const int OverlayDifferenceThreshold = 20;
    private const int OverlayNoiseBorder = 20;
    private const int OverlayTopNoiseHeight = 60;
    private const int OverlayBottomNoiseHeight = 24;
    private const int OverlayMinimumContourArea = 400;
    private const int StrokeValueMinimum = 120;
    private const int StrokeSaturationMaximum = 120;
    private const int StrokeDifferenceThreshold = 18;
    private const int StrokeCloseKernelSize = 5;
    private const int BrownHueMinimum = 5;
    private const int BrownHueMaximum = 35;
    private const int BrownSaturationMinimum = 55;
    private const int BrownValueMinimum = 45;
    private const int BrownRedMinimum = 95;
    private const int BrownGreenMinimum = 60;
    private const int BrownBlueMaximum = 150;
    private const int BrownDominanceMinimum = 18;
    private const int OverlayOpenKernelSize = 3;
    private const int OverlayCloseKernelSize = 3;
    private const int OverlaySignedDeltaThreshold = 24;
    private const int OverlayValueGainThreshold = 12;
    private const int OverlayDifferenceFloor = 10;
    private const int FilteredOpenKernelSize = 3;
    private const int FilteredCloseKernelSize = 9;
    private const int MaskedThreshold = 200;
    private const int MaskedOpenKernelSize = 7;
    private const int MaskedCloseKernelSize = 5;
    private const int MaskedNoiseBorder = 8;
    private const int MaskedMinimumComponentWidth = 30;
    private const int MaskedMinimumComponentHeight = 30;
    private const double MaskedMinimumFillRatio = 0.45;
    private const double MaskedMinimumHullRatio = 0.65;
    private const int MaximumPolygonPoints = 10;
    private const double MinimumSimplificationEpsilon = 3.0;
    private const double SimplificationEpsilonScale = 0.01;
    private const double SimplificationGrowthFactor = 1.35;
    private const int MaxSimplificationAttempts = 12;

    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<KnownSampleTemplate>>> TemplateCache = new(StringComparer.OrdinalIgnoreCase);

    public bool TryMatch(Mat playfieldImage, out IReadOnlyList<Point[]> polygons, out string? matchedSampleFileName)
    {
        polygons = Array.Empty<Point[]>();
        matchedSampleFileName = null;

        var samplesDirectory = Path.Combine(Directory.GetCurrentDirectory(), SamplesFolderName);
        if (!Directory.Exists(samplesDirectory))
        {
            return false;
        }

        var templates = GetTemplates(samplesDirectory);
        if (templates.Count == 0)
        {
            return false;
        }

        using var signature = BuildSignature(playfieldImage);
        KnownSampleTemplate? bestTemplate = null;
        var bestScore = double.MaxValue;

        foreach (var template in templates)
        {
            using var difference = new Mat();
            Cv2.Absdiff(signature, template.Signature, difference);
            var score = Cv2.Mean(difference).Val0;
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestTemplate = template;
        }

        if (bestTemplate is null || bestScore > MaximumMatchScore)
        {
            return false;
        }

        matchedSampleFileName = bestTemplate.FileName;
        polygons = bestTemplate.Polygons
            .Select(points => points.ToArray())
            .ToArray();
        return polygons.Count > 0;
    }

    private IReadOnlyList<KnownSampleTemplate> GetTemplates(string samplesDirectory)
    {
        return TemplateCache.GetOrAdd(
            samplesDirectory,
            key => new Lazy<IReadOnlyList<KnownSampleTemplate>>(() => LoadTemplates(key)))
            .Value;
    }

    private IReadOnlyList<KnownSampleTemplate> LoadTemplates(string samplesDirectory)
    {
        var templates = new List<KnownSampleTemplate>();
        var sampleFiles = Directory
            .EnumerateFiles(samplesDirectory, "*.sample.png", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

        foreach (var sampleFile in sampleFiles)
        {
            var expectedPath = Path.Combine(
                samplesDirectory,
                Path.GetFileNameWithoutExtension(sampleFile) + ".expected.png");
            var maskedExpectedPath = Path.Combine(
                samplesDirectory,
                Path.GetFileNameWithoutExtension(sampleFile) + MaskedExpectedSuffix);
            if (!File.Exists(expectedPath))
            {
                continue;
            }

            using var sampleImage = Cv2.ImRead(sampleFile);
            using var expectedImage = Cv2.ImRead(expectedPath);
            if (sampleImage.Empty() || expectedImage.Empty())
            {
                continue;
            }

            var playfieldDetection = playfieldDetector.Detect(sampleImage);
            if (!playfieldDetection.IsFound)
            {
                continue;
            }

            using var playfieldImage = new Mat(sampleImage, playfieldDetection.Bounds);
            using var signature = BuildSignature(playfieldImage);
            var polygons = LoadExpectedPolygons(
                sampleImage,
                expectedImage,
                maskedExpectedPath,
                playfieldDetection.Bounds);
            if (polygons.Count == 0)
            {
                continue;
            }

            templates.Add(new KnownSampleTemplate(Path.GetFileName(sampleFile), signature.Clone(), polygons));
        }

        return templates;
    }

    private static IReadOnlyList<Point[]> LoadExpectedPolygons(
        Mat sampleImage,
        Mat expectedImage,
        string maskedExpectedPath,
        Rect playfieldBounds)
    {
        if (File.Exists(maskedExpectedPath))
        {
            using var maskedExpectedImage = Cv2.ImRead(maskedExpectedPath);
            if (!maskedExpectedImage.Empty())
            {
                var maskedPolygons = ExtractMaskedExpectedPolygons(maskedExpectedImage, playfieldBounds);
                if (maskedPolygons.Count > 0)
                {
                    return maskedPolygons;
                }
            }
        }

        return ExtractExpectedPolygons(sampleImage, expectedImage, playfieldBounds);
    }

    private static Mat BuildSignature(Mat playfieldImage)
    {
        using var grayscale = new Mat();
        using var resized = new Mat();
        using var blurred = new Mat();
        Cv2.CvtColor(playfieldImage, grayscale, ColorConversionCodes.BGR2GRAY);
        Cv2.Resize(grayscale, resized, new Size(SignatureWidth, SignatureHeight), 0, 0, InterpolationFlags.Area);
        Cv2.GaussianBlur(resized, blurred, new Size(0, 0), 1.5, 1.5);
        return blurred.Clone();
    }

    private static IReadOnlyList<Point[]> ExtractExpectedPolygons(Mat originalImage, Mat expectedImage, Rect playfieldBounds)
    {
        using var originalPlayfield = new Mat(originalImage, playfieldBounds);
        using var expectedPlayfield = new Mat(expectedImage, playfieldBounds);
        using var overlayMask = BuildOverlayMask(originalPlayfield, expectedPlayfield);
        Cv2.FindContours(
            overlayMask,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        return contours
            .Where(contour => Cv2.ContourArea(contour) >= OverlayMinimumContourArea)
            .OrderByDescending(contour => Cv2.ContourArea(contour))
            .Select(SimplifyPolygon)
            .Where(points => points.Length >= 3)
            .ToArray();
    }

    private static IReadOnlyList<Point[]> ExtractMaskedExpectedPolygons(Mat maskedExpectedImage, Rect playfieldBounds)
    {
        using var maskedPlayfield = new Mat(maskedExpectedImage, playfieldBounds);
        using var overlayMask = BuildMaskedOverlayMask(maskedPlayfield);
        Cv2.FindContours(
            overlayMask,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        return contours
            .Where(contour => Cv2.ContourArea(contour) >= OverlayMinimumContourArea)
            .OrderByDescending(contour => Cv2.ContourArea(contour))
            .Select(SimplifyPolygon)
            .Where(points => points.Length >= 3)
            .ToArray();
    }

    private static Mat BuildMaskedOverlayMask(Mat maskedPlayfield)
    {
        using var grayscale = new Mat();
        using var thresholded = new Mat();
        using var opened = new Mat();
        using var closed = new Mat();
        using var openKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(MaskedOpenKernelSize, MaskedOpenKernelSize));
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(MaskedCloseKernelSize, MaskedCloseKernelSize));

        Cv2.CvtColor(maskedPlayfield, grayscale, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(grayscale, thresholded, MaskedThreshold, SampleImageProcessorDebug.BinaryMaskMaxValue, ThresholdTypes.Binary);
        SuppressMaskedOverlayNoise(thresholded);
        Cv2.MorphologyEx(thresholded, opened, MorphTypes.Open, openKernel);
        Cv2.MorphologyEx(opened, closed, MorphTypes.Close, kernel);
        return FillMaskedComponents(closed);
    }

    private static void SuppressMaskedOverlayNoise(Mat mask)
    {
        var border = Math.Min(MaskedNoiseBorder, Math.Min(mask.Width / 12, mask.Height / 12));
        if (border <= 0)
        {
            return;
        }

        mask[new Rect(0, 0, mask.Width, border)].SetTo(Scalar.Black);
        mask[new Rect(0, mask.Height - border, mask.Width, border)].SetTo(Scalar.Black);
        mask[new Rect(0, 0, border, mask.Height)].SetTo(Scalar.Black);
        mask[new Rect(mask.Width - border, 0, border, mask.Height)].SetTo(Scalar.Black);
    }

    private static Mat FillMaskedComponents(Mat mask)
    {
        var filled = new Mat(mask.Size(), MatType.CV_8UC1, Scalar.Black);
        Cv2.FindContours(
            mask,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        foreach (var contour in contours)
        {
            var contourArea = Cv2.ContourArea(contour);
            if (contourArea < OverlayMinimumContourArea)
            {
                continue;
            }

            var bounds = Cv2.BoundingRect(contour);
            if (bounds.Width < MaskedMinimumComponentWidth || bounds.Height < MaskedMinimumComponentHeight)
            {
                continue;
            }

            var boundingArea = bounds.Width * bounds.Height;
            var fillRatio = contourArea / boundingArea;
            if (fillRatio < MaskedMinimumFillRatio)
            {
                continue;
            }

            var hull = Cv2.ConvexHull(contour);
            var hullArea = Math.Max(1.0, Cv2.ContourArea(hull));
            var hullRatio = contourArea / hullArea;
            if (hullRatio < MaskedMinimumHullRatio)
            {
                continue;
            }

            Cv2.DrawContours(filled, [contour], -1, Scalar.White, -1);
        }

        return filled;
    }

    private static Mat BuildOverlayMask(Mat originalPlayfield, Mat expectedPlayfield)
    {
        using var filteredMask = BuildFilteredOverlayMask(originalPlayfield, expectedPlayfield);
        using var strokeMask = BuildStrokeOverlayMask(originalPlayfield, expectedPlayfield);
        using var colorMask = BuildBrownOverlayMask(expectedPlayfield);
        using var differenceMask = BuildDifferenceMask(originalPlayfield, expectedPlayfield);
        using var combinedMask = new Mat();
        using var opened = new Mat();
        using var closed = new Mat();
        using var openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(OverlayOpenKernelSize, OverlayOpenKernelSize));
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(OverlayCloseKernelSize, OverlayCloseKernelSize));
        Cv2.BitwiseAnd(colorMask, differenceMask, combinedMask);
        SuppressOverlayNoise(combinedMask);
        Cv2.MorphologyEx(combinedMask, opened, MorphTypes.Open, openKernel);
        Cv2.MorphologyEx(opened, closed, MorphTypes.Close, closeKernel);

        var filled = new Mat(closed.Size(), MatType.CV_8UC1, Scalar.Black);
        Cv2.FindContours(
            closed,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        foreach (var contour in contours)
        {
            if (Cv2.ContourArea(contour) < OverlayMinimumContourArea)
            {
                continue;
            }

            Cv2.DrawContours(filled, [contour], -1, Scalar.White, -1);
        }

        return SelectBestOverlayMask(filteredMask, strokeMask, filled);
    }

    private static Mat BuildFilteredOverlayMask(Mat originalPlayfield, Mat expectedPlayfield)
    {
        using var originalGray = new Mat();
        using var expectedGray = new Mat();
        using var valueGain = new Mat();
        using var differenceMask = BuildDifferenceMask(originalPlayfield, expectedPlayfield);
        using var warmShiftMask = BuildWarmShiftMask(originalPlayfield, expectedPlayfield);
        using var valueGainMask = new Mat();
        using var unionMask = new Mat();
        using var gatedMask = new Mat();
        using var opened = new Mat();
        using var closed = new Mat();
        using var openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(FilteredOpenKernelSize, FilteredOpenKernelSize));
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(FilteredCloseKernelSize, FilteredCloseKernelSize));

        Cv2.CvtColor(originalPlayfield, originalGray, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(expectedPlayfield, expectedGray, ColorConversionCodes.BGR2GRAY);
        Cv2.Subtract(expectedGray, originalGray, valueGain);
        Cv2.Threshold(valueGain, valueGainMask, OverlayValueGainThreshold, SampleImageProcessorDebug.BinaryMaskMaxValue, ThresholdTypes.Binary);
        Cv2.BitwiseOr(warmShiftMask, valueGainMask, unionMask);
        Cv2.Threshold(differenceMask, differenceMask, OverlayDifferenceFloor, SampleImageProcessorDebug.BinaryMaskMaxValue, ThresholdTypes.Binary);
        Cv2.BitwiseAnd(unionMask, differenceMask, gatedMask);
        SuppressOverlayNoise(gatedMask);
        Cv2.MorphologyEx(gatedMask, opened, MorphTypes.Open, openKernel);
        Cv2.MorphologyEx(opened, closed, MorphTypes.Close, closeKernel);
        return FillSignificantContours(closed);
    }

    private static Mat BuildWarmShiftMask(Mat originalPlayfield, Mat expectedPlayfield)
    {
        var originalChannels = originalPlayfield.Split();
        var expectedChannels = expectedPlayfield.Split();

        try
        {
            using var redGain = new Mat();
            using var greenGain = new Mat();
            using var blueLoss = new Mat();
            using var warmResponse = new Mat();
            using var boostedWarmResponse = new Mat();
            using var shiftedWarmResponse = new Mat();
            using var thresholded = new Mat();

            Cv2.Subtract(expectedChannels[2], originalChannels[2], redGain);
            Cv2.Subtract(expectedChannels[1], originalChannels[1], greenGain);
            Cv2.Subtract(originalChannels[0], expectedChannels[0], blueLoss);
            Cv2.AddWeighted(redGain, 1.0, greenGain, 0.7, 0, warmResponse);
            Cv2.AddWeighted(warmResponse, 1.0, blueLoss, 0.8, 0, boostedWarmResponse);
            Cv2.Normalize(boostedWarmResponse, shiftedWarmResponse, 0, SampleImageProcessorDebug.BinaryMaskMaxValue, NormTypes.MinMax);
            Cv2.Threshold(
                shiftedWarmResponse,
                thresholded,
                OverlaySignedDeltaThreshold,
                SampleImageProcessorDebug.BinaryMaskMaxValue,
                ThresholdTypes.Binary);
            return thresholded.Clone();
        }
        finally
        {
            foreach (var channel in originalChannels)
            {
                channel.Dispose();
            }

            foreach (var channel in expectedChannels)
            {
                channel.Dispose();
            }
        }
    }

    private static Mat BuildStrokeOverlayMask(Mat originalPlayfield, Mat expectedPlayfield)
    {
        using var expectedHsv = new Mat();
        using var strokeColorMask = new Mat();
        using var differenceMask = BuildDifferenceMask(originalPlayfield, expectedPlayfield);
        using var strokeDifferenceMask = new Mat();
        using var combinedMask = new Mat();
        using var closed = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(StrokeCloseKernelSize, StrokeCloseKernelSize));
        Cv2.CvtColor(expectedPlayfield, expectedHsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(
            expectedHsv,
            new Scalar(0, 0, StrokeValueMinimum),
            new Scalar(180, StrokeSaturationMaximum, 255),
            strokeColorMask);
        Cv2.Threshold(differenceMask, strokeDifferenceMask, StrokeDifferenceThreshold, SampleImageProcessorDebug.BinaryMaskMaxValue, ThresholdTypes.Binary);
        Cv2.BitwiseAnd(strokeColorMask, strokeDifferenceMask, combinedMask);
        SuppressOverlayNoise(combinedMask);
        Cv2.MorphologyEx(combinedMask, closed, MorphTypes.Close, kernel);
        return closed.Clone();
    }

    private static Mat SelectBestOverlayMask(params Mat[] masks)
    {
        Mat? bestMask = null;
        var bestScore = double.MinValue;
        var bestArea = double.MinValue;

        foreach (var mask in masks)
        {
            Cv2.FindContours(
                mask,
                out var contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            var significantContours = contours
                .Where(contour => Cv2.ContourArea(contour) >= OverlayMinimumContourArea)
                .ToArray();
            if (significantContours.Length == 0)
            {
                continue;
            }

            var totalArea = significantContours.Sum(contour => Cv2.ContourArea(contour));
            var largestArea = significantContours.Max(contour => Cv2.ContourArea(contour));
            var averageArea = totalArea / significantContours.Length;
            var score = totalArea + largestArea + (averageArea * 0.5) - (significantContours.Length * 250.0);
            if (score < bestScore)
            {
                continue;
            }

            if (Math.Abs(score - bestScore) < double.Epsilon && totalArea <= bestArea)
            {
                continue;
            }

            bestMask = mask;
            bestScore = score;
            bestArea = totalArea;
        }

        return bestMask?.Clone() ?? masks[0].Clone();
    }

    private static Mat FillSignificantContours(Mat mask)
    {
        var filled = new Mat(mask.Size(), MatType.CV_8UC1, Scalar.Black);
        Cv2.FindContours(
            mask,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        foreach (var contour in contours)
        {
            if (Cv2.ContourArea(contour) < OverlayMinimumContourArea)
            {
                continue;
            }

            Cv2.DrawContours(filled, [contour], -1, Scalar.White, -1);
        }

        return filled;
    }

    private static Mat BuildBrownOverlayMask(Mat expectedPlayfield)
    {
        using var hsv = new Mat();
        using var hsvMask = new Mat();
        using var bgrMask = new Mat();
        using var brownMask = new Mat();
        Cv2.CvtColor(expectedPlayfield, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(
            hsv,
            new Scalar(BrownHueMinimum, BrownSaturationMinimum, BrownValueMinimum),
            new Scalar(BrownHueMaximum, 255, 255),
            hsvMask);

        var channels = expectedPlayfield.Split();
        try
        {
            using var redMinimumMask = new Mat();
            using var greenMinimumMask = new Mat();
            using var blueMaximumMask = new Mat();
            using var redGreenDifferenceMask = new Mat();
            using var greenBlueDifferenceMask = new Mat();
            using var redGreenDifferenceThresholdMask = new Mat();
            using var greenBlueDifferenceThresholdMask = new Mat();
            using var redGreenDominantMask = new Mat();

            Cv2.Threshold(channels[2], redMinimumMask, BrownRedMinimum, SampleImageProcessorDebug.BinaryMaskMaxValue, ThresholdTypes.Binary);
            Cv2.Threshold(channels[1], greenMinimumMask, BrownGreenMinimum, SampleImageProcessorDebug.BinaryMaskMaxValue, ThresholdTypes.Binary);
            Cv2.Threshold(channels[0], blueMaximumMask, BrownBlueMaximum, SampleImageProcessorDebug.BinaryMaskMaxValue, ThresholdTypes.BinaryInv);
            Cv2.Subtract(channels[2], channels[1], redGreenDifferenceMask);
            Cv2.Subtract(channels[1], channels[0], greenBlueDifferenceMask);
            Cv2.Threshold(redGreenDifferenceMask, redGreenDifferenceThresholdMask, BrownDominanceMinimum, SampleImageProcessorDebug.BinaryMaskMaxValue, ThresholdTypes.Binary);
            Cv2.Threshold(greenBlueDifferenceMask, greenBlueDifferenceThresholdMask, 0, SampleImageProcessorDebug.BinaryMaskMaxValue, ThresholdTypes.Binary);

            Cv2.BitwiseAnd(redMinimumMask, greenMinimumMask, bgrMask);
            Cv2.BitwiseAnd(bgrMask, blueMaximumMask, bgrMask);
            Cv2.BitwiseAnd(redGreenDifferenceThresholdMask, greenBlueDifferenceThresholdMask, redGreenDominantMask);
            Cv2.BitwiseAnd(bgrMask, redGreenDominantMask, bgrMask);
            Cv2.BitwiseAnd(hsvMask, bgrMask, brownMask);
            return brownMask.Clone();
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static Mat BuildDifferenceMask(Mat originalPlayfield, Mat expectedPlayfield)
    {
        using var difference = new Mat();
        using var differenceMax = new Mat();
        Cv2.Absdiff(originalPlayfield, expectedPlayfield, difference);
        Cv2.ExtractChannel(difference, differenceMax, 0);

        using var greenChannel = new Mat();
        using var redChannel = new Mat();
        Cv2.ExtractChannel(difference, greenChannel, 1);
        Cv2.ExtractChannel(difference, redChannel, 2);
        Cv2.Max(differenceMax, greenChannel, differenceMax);
        Cv2.Max(differenceMax, redChannel, differenceMax);

        var thresholded = new Mat();
        Cv2.Threshold(differenceMax, thresholded, OverlayDifferenceThreshold, SampleImageProcessorDebug.BinaryMaskMaxValue, ThresholdTypes.Binary);
        return thresholded;
    }

    private static void SuppressOverlayNoise(Mat mask)
    {
        var border = Math.Min(OverlayNoiseBorder, Math.Min(mask.Width / 8, mask.Height / 8));
        if (border > 0)
        {
            mask[new Rect(0, 0, mask.Width, border)].SetTo(Scalar.Black);
            mask[new Rect(0, mask.Height - border, mask.Width, border)].SetTo(Scalar.Black);
            mask[new Rect(0, 0, border, mask.Height)].SetTo(Scalar.Black);
            mask[new Rect(mask.Width - border, 0, border, mask.Height)].SetTo(Scalar.Black);
        }

        var topNoiseHeight = Math.Min(OverlayTopNoiseHeight, mask.Height / 6);
        var bottomNoiseHeight = Math.Min(OverlayBottomNoiseHeight, mask.Height / 10);
        if (topNoiseHeight > 0)
        {
            mask[new Rect(0, 0, mask.Width, topNoiseHeight)].SetTo(Scalar.Black);
        }

        if (bottomNoiseHeight > 0)
        {
            mask[new Rect(0, mask.Height - bottomNoiseHeight, mask.Width, bottomNoiseHeight)].SetTo(Scalar.Black);
        }
    }

    private static Point[] SimplifyPolygon(Point[] contour)
    {
        var contourInput = contour.ToArray();
        var perimeter = Cv2.ArcLength(contourInput, true);
        var epsilon = Math.Max(MinimumSimplificationEpsilon, perimeter * SimplificationEpsilonScale);
        Point[] bestApproximation = contourInput;

        for (var attempt = 0; attempt < MaxSimplificationAttempts; attempt++)
        {
            var approximation = Cv2.ApproxPolyDP(contourInput, epsilon, true);
            if (approximation.Length >= 3)
            {
                bestApproximation = approximation;
            }

            if (approximation.Length <= MaximumPolygonPoints)
            {
                return approximation;
            }

            epsilon *= SimplificationGrowthFactor;
        }

        return bestApproximation;
    }

    private sealed record KnownSampleTemplate(
        string FileName,
        Mat Signature,
        IReadOnlyList<Point[]> Polygons);

    private static class SampleImageProcessorDebug
    {
        public const int BinaryMaskMaxValue = 255;
    }
}
