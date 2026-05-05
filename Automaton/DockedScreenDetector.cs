using OpenCvSharp;

namespace Automaton;

internal sealed class DockedScreenDetector
{
    private const int BinaryMaskMaxValue = 255;
    private const double MinimumFocusedEntryBlueRatio = 0.18;
    private const int MinimumUndockButtonWidth = 280;
    private const int MinimumUndockButtonHeight = 30;
    private const double MinimumUndockButtonBlueRatio = 0.12;
    private const int MinimumOreContourArea = 450;
    private const int MinimumOreContourWidth = 24;
    private const int MinimumOreContourHeight = 24;
    private static readonly Scalar BlueUiMinimum = new(85, 35, 25);
    private static readonly Scalar BlueUiMaximum = new(110, 220, 140);

    public DockedScreenAnalysis Analyze(Mat screen)
    {
        if (screen.Empty())
        {
            return DockedScreenAnalysis.NotFound;
        }

        var imageSize = screen.Size();
        var undockButtonBounds = LocateUndockButton(screen);
        var miningHoldEntryBounds = BuildMiningHoldEntryBounds(imageSize);
        var itemHangarEntryBounds = BuildItemHangarEntryBounds(imageSize);
        var miningHoldFocused = HasFocusedEntryHighlight(screen, miningHoldEntryBounds);
        var itemHangarFocused = HasFocusedEntryHighlight(screen, itemHangarEntryBounds);
        var miningHoldContent = miningHoldFocused
            ? DetectMiningHoldContent(screen, BuildMiningHoldContentBounds(imageSize))
            : MiningHoldContentState.Unknown;

        return new DockedScreenAnalysis(
            undockButtonBounds is not null,
            undockButtonBounds,
            miningHoldEntryBounds,
            itemHangarEntryBounds,
            miningHoldFocused,
            itemHangarFocused,
            miningHoldContent);
    }

    private static Rect? LocateUndockButton(Mat screen)
    {
        var searchBounds = BuildUndockButtonSearchBounds(screen.Size());
        using var searchRegion = new Mat(screen, searchBounds);
        using var blueMask = BuildBlueUiMask(searchRegion);
        Cv2.FindContours(
            blueMask,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        Rect? bestBounds = null;
        var bestArea = 0;
        foreach (var contour in contours)
        {
            var bounds = Cv2.BoundingRect(contour);
            if (bounds.Width < MinimumUndockButtonWidth ||
                bounds.Height < MinimumUndockButtonHeight)
            {
                continue;
            }

            using var candidateMask = new Mat(blueMask, bounds);
            var bluePixels = Cv2.CountNonZero(candidateMask);
            var area = bounds.Width * bounds.Height;
            if (bluePixels < area * MinimumUndockButtonBlueRatio || area <= bestArea)
            {
                continue;
            }

            bestArea = area;
            bestBounds = new Rect(
                searchBounds.X + bounds.X,
                searchBounds.Y + bounds.Y,
                bounds.Width,
                bounds.Height);
        }

        return bestBounds;
    }

    private static bool HasFocusedEntryHighlight(Mat screen, Rect entryBounds)
    {
        using var entry = new Mat(screen, entryBounds);
        using var blueMask = BuildBlueUiMask(entry);
        var bluePixels = Cv2.CountNonZero(blueMask);
        return bluePixels >= entryBounds.Width * entryBounds.Height * MinimumFocusedEntryBlueRatio;
    }

    private static MiningHoldContentState DetectMiningHoldContent(Mat screen, Rect contentBounds)
    {
        using var content = new Mat(screen, contentBounds);
        using var gray = new Mat();
        using var brightMask = new Mat();
        using var closed = new Mat();
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));

        Cv2.CvtColor(content, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(gray, brightMask, 115, BinaryMaskMaxValue, ThresholdTypes.Binary);
        Cv2.MorphologyEx(brightMask, closed, MorphTypes.Close, closeKernel);
        Cv2.FindContours(
            closed,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < MinimumOreContourArea)
            {
                continue;
            }

            var bounds = Cv2.BoundingRect(contour);
            if (bounds.Width >= MinimumOreContourWidth &&
                bounds.Height >= MinimumOreContourHeight)
            {
                return MiningHoldContentState.ContainsOre;
            }
        }

        return MiningHoldContentState.Empty;
    }

    private static Mat BuildBlueUiMask(Mat image)
    {
        using var hsv = new Mat();
        var mask = new Mat();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, BlueUiMinimum, BlueUiMaximum, mask);
        return mask;
    }

    private static Rect BuildUndockButtonSearchBounds(Size imageSize)
    {
        return BuildRelativeBounds(imageSize, 0.75, 0.13, 0.24, 0.18);
    }

    private static Rect BuildMiningHoldEntryBounds(Size imageSize)
    {
        return BuildRelativeBounds(imageSize, 0.025, 0.824, 0.095, 0.026);
    }

    private static Rect BuildItemHangarEntryBounds(Size imageSize)
    {
        return BuildRelativeBounds(imageSize, 0.025, 0.867, 0.095, 0.026);
    }

    private static Rect BuildMiningHoldContentBounds(Size imageSize)
    {
        return BuildRelativeBounds(imageSize, 0.125, 0.805, 0.070, 0.115);
    }

    private static Rect BuildRelativeBounds(
        Size imageSize,
        double leftRatio,
        double topRatio,
        double widthRatio,
        double heightRatio)
    {
        var left = (int)Math.Round(imageSize.Width * leftRatio);
        var top = (int)Math.Round(imageSize.Height * topRatio);
        var width = (int)Math.Round(imageSize.Width * widthRatio);
        var height = (int)Math.Round(imageSize.Height * heightRatio);

        left = Math.Clamp(left, 0, Math.Max(0, imageSize.Width - 1));
        top = Math.Clamp(top, 0, Math.Max(0, imageSize.Height - 1));
        width = Math.Clamp(width, 1, imageSize.Width - left);
        height = Math.Clamp(height, 1, imageSize.Height - top);
        return new Rect(left, top, width, height);
    }
}

internal sealed record DockedScreenAnalysis(
    bool IsDocked,
    Rect? UndockButtonBounds,
    Rect? MiningHoldEntryBounds,
    Rect? ItemHangarEntryBounds,
    bool MiningHoldFocused,
    bool ItemHangarFocused,
    MiningHoldContentState MiningHoldContent)
{
    public static DockedScreenAnalysis NotFound { get; } = new(
        false,
        null,
        null,
        null,
        false,
        false,
        MiningHoldContentState.Unknown);
}

internal enum MiningHoldContentState
{
    Unknown,
    Empty,
    ContainsOre
}
