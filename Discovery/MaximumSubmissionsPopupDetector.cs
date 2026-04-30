using OpenCvSharp;

namespace Discovery;

internal sealed class MaximumSubmissionsPopupDetector
{
    private const double SearchLeftRatio = 0.56;
    private const double SearchTopRatio = 0.62;
    private const double SearchRightRatio = 0.92;
    private const double SearchBottomRatio = 0.91;
    private const double PopupHeightToWidthRatio = 0.64;
    private const double PopupCandidateWidthGrowth = 1.15;
    private const int TitleWhiteMinimum = 2_500;
    private const int BodyWhiteMinimum = 2_200;
    private const int IconWhiteMinimum = 600;
    private const int ButtonCyanMinimum = 700;
    private const int ButtonWhiteMinimum = 50;
    private const int ButtonWhiteMaximum = 800;
    private const int MinimumBodyTextBands = 3;
    private const int BodyTextBandRowWhiteMinimum = 8;
    private const int MinimumBodyTextBandHeight = 3;
    private const int TitleLineWhiteMinimum = 700;
    private const double MinimumInformationIconContourArea = 450.0;
    private const double MaximumInformationIconContourArea = 8_000.0;
    private const int MinimumInformationIconContourWidth = 32;
    private const int MinimumInformationIconContourHeight = 32;
    private const int MinimumInformationIconContourMargin = 2;
    private const double MinimumInformationIconAspectRatio = 0.50;
    private const double MaximumInformationIconAspectRatio = 1.80;
    private const double MaximumFilledSquareIconFillRatio = 0.88;
    private const double MaximumBodyWhiteContourArea = 1_200.0;
    private const double TitleWhiteMaximumDensity = 0.22;
    private const double BodyWhiteMaximumDensity = 0.16;
    private const double IconWhiteMaximumDensity = 0.25;
    private const int MinimumSearchDimension = 1;
    private const int BinaryMaskMaxValue = 255;
    private const int MinimumPopupCandidateWidth = 480;
    private const int MaximumPopupCandidateWidth = 1_600;
    private const int MinimumPopupCandidateStep = 32;
    private const string DebugOverlayText = "Maximum submissions popup detected";
    private const double DebugOverlayTextScale = 0.8;
    private const int DebugOverlayTextThickness = 2;
    private const int DebugOverlayLeftPadding = 30;
    private const int DebugOverlayTopPadding = 40;
    private static readonly Scalar WhiteMinimum = new(180, 180, 180);
    private static readonly Scalar WhiteMaximum = new(255, 255, 255);
    private static readonly Scalar IconMinimum = new(135, 135, 135);
    private static readonly Scalar CyanMinimum = new(80, 40, 45);
    private static readonly Scalar CyanMaximum = new(105, 255, 230);
    private static readonly Scalar DebugOverlayTextColor = new(80, 120, 255);

    public bool Detect(string imagePath)
    {
        using var image = Cv2.ImRead(imagePath);
        return Detect(image);
    }

    public bool DetectAndDrawDebugOverlay(string imagePath)
    {
        using var image = Cv2.ImRead(imagePath);
        if (!Detect(image))
        {
            return false;
        }

        DrawDebugOverlay(image);
        Cv2.ImWrite(imagePath, image);
        return true;
    }

    public bool Detect(Mat image)
    {
        if (image.Empty())
        {
            return false;
        }

        using var masks = PopupEvidenceMasks.Create(image);
        return BuildCandidateBounds(image.Size()).Any(candidate => ContainsPopupEvidence(masks, candidate));
    }

    private static IEnumerable<Rect> BuildCandidateBounds(Size imageSize)
    {
        yield return BuildLegacySearchBounds(imageSize);

        foreach (var candidateSize in BuildCandidateSizes(imageSize))
        {
            var step = Math.Max(MinimumPopupCandidateStep, candidateSize.Width / 10);
            for (var top = 0; top <= imageSize.Height - candidateSize.Height; top += step)
            {
                for (var left = 0; left <= imageSize.Width - candidateSize.Width; left += step)
                {
                    yield return new Rect(left, top, candidateSize.Width, candidateSize.Height);
                }
            }
        }
    }

    private static IEnumerable<Size> BuildCandidateSizes(Size imageSize)
    {
        if (imageSize.Width < MinimumPopupCandidateWidth ||
            imageSize.Height < (int)Math.Round(MinimumPopupCandidateWidth * PopupHeightToWidthRatio))
        {
            yield break;
        }

        var maximumWidth = Math.Min(MaximumPopupCandidateWidth, imageSize.Width);
        for (var width = MinimumPopupCandidateWidth; width <= maximumWidth; width = Math.Max(width + 1, (int)Math.Round(width * PopupCandidateWidthGrowth)))
        {
            var height = (int)Math.Round(width * PopupHeightToWidthRatio);
            if (height <= imageSize.Height)
            {
                yield return new Size(width, height);
            }
        }
    }

    private static Rect BuildLegacySearchBounds(Size imageSize)
    {
        var left = (int)Math.Round(imageSize.Width * SearchLeftRatio);
        var top = (int)Math.Round(imageSize.Height * SearchTopRatio);
        var right = (int)Math.Round(imageSize.Width * SearchRightRatio);
        var bottom = (int)Math.Round(imageSize.Height * SearchBottomRatio);
        return BuildClampedBounds(left, top, right - left, bottom - top, imageSize);
    }

    private static bool ContainsPopupEvidence(PopupEvidenceMasks masks, Rect candidate)
    {
        var buttonBounds = BuildButtonBounds(candidate);
        var buttonWhitePixels = masks.CountWhitePixels(buttonBounds);
        var buttonEvidenceFound = masks.CountCyanPixels(buttonBounds) >= ButtonCyanMinimum ||
                                  buttonWhitePixels is >= ButtonWhiteMinimum and <= ButtonWhiteMaximum;
        var titleBounds = BuildTitleBounds(candidate);
        var bodyBounds = BuildBodyBounds(candidate);
        var iconBounds = BuildIconBounds(candidate);
        return HasTitleEvidence(masks, titleBounds) &&
               HasWhitePixelEvidence(masks, bodyBounds, BodyWhiteMinimum, BodyWhiteMaximumDensity) &&
               masks.CountWhiteTextBands(bodyBounds) >= MinimumBodyTextBands &&
               HasInformationIconEvidence(masks, iconBounds) &&
               masks.GetLargestWhiteContourArea(bodyBounds) <= MaximumBodyWhiteContourArea &&
               buttonEvidenceFound;
    }

    private static bool HasTitleEvidence(PopupEvidenceMasks masks, Rect titleBounds)
    {
        var firstLineBounds = BuildClampedBounds(
            titleBounds.X,
            titleBounds.Y,
            titleBounds.Width,
            titleBounds.Height / 2,
            new Size(titleBounds.Right, titleBounds.Bottom));
        var secondLineBounds = BuildClampedBounds(
            titleBounds.X,
            titleBounds.Y + titleBounds.Height / 2,
            titleBounds.Width,
            titleBounds.Height - titleBounds.Height / 2,
            new Size(titleBounds.Right, titleBounds.Bottom));

        return HasWhitePixelEvidence(masks, titleBounds, TitleWhiteMinimum, TitleWhiteMaximumDensity) &&
               masks.CountWhitePixels(firstLineBounds) >= TitleLineWhiteMinimum &&
               masks.CountWhitePixels(secondLineBounds) >= TitleLineWhiteMinimum;
    }

    private static bool HasInformationIconEvidence(PopupEvidenceMasks masks, Rect iconBounds)
    {
        var iconWhitePixels = masks.CountIconPixels(iconBounds);
        var iconArea = Math.Max(1, iconBounds.Width * iconBounds.Height);
        if (iconWhitePixels < IconWhiteMinimum ||
            iconWhitePixels > iconArea * IconWhiteMaximumDensity)
        {
            return false;
        }

        var icon = masks.GetLargestIconContour(iconBounds);
        if (icon is null)
        {
            return false;
        }

        var area = icon.Value.Area;
        if (area is < MinimumInformationIconContourArea or > MaximumInformationIconContourArea)
        {
            return false;
        }

        var bounds = icon.Value.Bounds;
        if (bounds.Width < MinimumInformationIconContourWidth ||
            bounds.Height < MinimumInformationIconContourHeight)
        {
            return false;
        }

        if (bounds.Left <= MinimumInformationIconContourMargin ||
            bounds.Top <= MinimumInformationIconContourMargin ||
            bounds.Right >= iconBounds.Width - MinimumInformationIconContourMargin ||
            bounds.Bottom >= iconBounds.Height - MinimumInformationIconContourMargin)
        {
            return false;
        }

        var aspectRatio = bounds.Width / (double)Math.Max(1, bounds.Height);
        if (aspectRatio is < MinimumInformationIconAspectRatio or > MaximumInformationIconAspectRatio)
        {
            return false;
        }

        var fillRatio = area / Math.Max(1, bounds.Width * bounds.Height);
        return fillRatio < MaximumFilledSquareIconFillRatio;
    }

    private static bool HasWhitePixelEvidence(
        PopupEvidenceMasks masks,
        Rect bounds,
        int minimumWhitePixels,
        double maximumWhiteDensity)
    {
        var whitePixels = masks.CountWhitePixels(bounds);
        var area = Math.Max(1, bounds.Width * bounds.Height);
        return whitePixels >= minimumWhitePixels &&
               whitePixels <= area * maximumWhiteDensity;
    }

    private static Rect BuildTitleBounds(Rect candidate)
    {
        return BuildRelativeBounds(candidate, 0.18, 0, 0.73, 0.28);
    }

    private static Rect BuildBodyBounds(Rect candidate)
    {
        return BuildRelativeBounds(candidate, 0.04, 0.30, 0.90, 0.40);
    }

    private static Rect BuildIconBounds(Rect candidate)
    {
        return BuildRelativeBounds(candidate, 0, 0, 0.30, 0.45);
    }

    private static Rect BuildButtonBounds(Rect candidate)
    {
        return BuildRelativeBounds(candidate, 0.04, 0.68, 0.90, 0.24);
    }

    private static Rect BuildRelativeBounds(
        Rect candidate,
        double leftRatio,
        double topRatio,
        double widthRatio,
        double heightRatio)
    {
        return BuildClampedBounds(
            candidate.X + (int)Math.Round(candidate.Width * leftRatio),
            candidate.Y + (int)Math.Round(candidate.Height * topRatio),
            (int)Math.Round(candidate.Width * widthRatio),
            (int)Math.Round(candidate.Height * heightRatio),
            new Size(candidate.Right, candidate.Bottom));
    }

    private static Rect BuildClampedBounds(int x, int y, int width, int height, Size containingSize)
    {
        var clampedX = Math.Clamp(x, 0, Math.Max(0, containingSize.Width - MinimumSearchDimension));
        var clampedY = Math.Clamp(y, 0, Math.Max(0, containingSize.Height - MinimumSearchDimension));
        var clampedWidth = Math.Clamp(width, MinimumSearchDimension, containingSize.Width - clampedX);
        var clampedHeight = Math.Clamp(height, MinimumSearchDimension, containingSize.Height - clampedY);
        return new Rect(clampedX, clampedY, clampedWidth, clampedHeight);
    }

    private static void DrawDebugOverlay(Mat image)
    {
        Cv2.PutText(
            image,
            DebugOverlayText,
            new Point(DebugOverlayLeftPadding, DebugOverlayTopPadding),
            HersheyFonts.HersheySimplex,
            DebugOverlayTextScale,
            DebugOverlayTextColor,
            DebugOverlayTextThickness,
            LineTypes.AntiAlias);
    }

    private sealed class PopupEvidenceMasks : IDisposable
    {
        private readonly Mat m_WhiteMask;
        private readonly Mat m_IconMask;
        private readonly Mat m_WhiteIntegral;
        private readonly Mat m_IconIntegral;
        private readonly Mat m_CyanIntegral;

        private PopupEvidenceMasks(Mat whiteMask, Mat iconMask, Mat whiteIntegral, Mat iconIntegral, Mat cyanIntegral)
        {
            m_WhiteMask = whiteMask;
            m_IconMask = iconMask;
            m_WhiteIntegral = whiteIntegral;
            m_IconIntegral = iconIntegral;
            m_CyanIntegral = cyanIntegral;
        }

        public static PopupEvidenceMasks Create(Mat image)
        {
            var whiteMask = new Mat();
            Cv2.InRange(image, WhiteMinimum, WhiteMaximum, whiteMask);
            var iconMask = new Mat();
            Cv2.InRange(image, IconMinimum, WhiteMaximum, iconMask);

            using var hsv = new Mat();
            using var cyanMask = new Mat();
            Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(hsv, CyanMinimum, CyanMaximum, cyanMask);

            var whiteIntegral = new Mat();
            var iconIntegral = new Mat();
            var cyanIntegral = new Mat();
            Cv2.Integral(whiteMask, whiteIntegral, MatType.CV_64F);
            Cv2.Integral(iconMask, iconIntegral, MatType.CV_64F);
            Cv2.Integral(cyanMask, cyanIntegral, MatType.CV_64F);
            return new PopupEvidenceMasks(whiteMask, iconMask, whiteIntegral, iconIntegral, cyanIntegral);
        }

        public int CountWhitePixels(Rect bounds)
        {
            return CountPixels(m_WhiteIntegral, bounds);
        }

        public int CountCyanPixels(Rect bounds)
        {
            return CountPixels(m_CyanIntegral, bounds);
        }

        public int CountIconPixels(Rect bounds)
        {
            return CountPixels(m_IconIntegral, bounds);
        }

        public int CountWhiteTextBands(Rect bounds)
        {
            var bands = 0;
            var currentBandHeight = 0;

            for (var row = bounds.Top; row < bounds.Bottom; row++)
            {
                var rowWhitePixels = CountWhitePixels(new Rect(bounds.Left, row, bounds.Width, 1));
                if (rowWhitePixels >= BodyTextBandRowWhiteMinimum)
                {
                    currentBandHeight++;
                    continue;
                }

                if (currentBandHeight >= MinimumBodyTextBandHeight)
                {
                    bands++;
                }

                currentBandHeight = 0;
            }

            if (currentBandHeight >= MinimumBodyTextBandHeight)
            {
                bands++;
            }

            return bands;
        }

        public double GetLargestWhiteContourArea(Rect bounds)
        {
            return GetLargestWhiteContour(bounds)?.Area ?? 0.0;
        }

        public WhiteContour? GetLargestWhiteContour(Rect bounds)
        {
            return GetLargestContour(m_WhiteMask, bounds);
        }

        public WhiteContour? GetLargestIconContour(Rect bounds)
        {
            return GetLargestContour(m_IconMask, bounds);
        }

        public void Dispose()
        {
            m_WhiteMask.Dispose();
            m_IconMask.Dispose();
            m_WhiteIntegral.Dispose();
            m_IconIntegral.Dispose();
            m_CyanIntegral.Dispose();
        }

        private static WhiteContour? GetLargestContour(Mat mask, Rect bounds)
        {
            using var region = new Mat(mask, bounds);
            using var regionCopy = region.Clone();
            Cv2.FindContours(
                regionCopy,
                out var contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            WhiteContour? largestContour = null;
            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                if (largestContour is not null && area <= largestContour.Value.Area)
                {
                    continue;
                }

                largestContour = new WhiteContour(area, Cv2.BoundingRect(contour));
            }

            return largestContour;
        }

        private static int CountPixels(Mat integral, Rect bounds)
        {
            var topLeft = integral.At<double>(bounds.Y, bounds.X);
            var topRight = integral.At<double>(bounds.Y, bounds.Right);
            var bottomLeft = integral.At<double>(bounds.Bottom, bounds.X);
            var bottomRight = integral.At<double>(bounds.Bottom, bounds.Right);
            return (int)Math.Round((bottomRight - bottomLeft - topRight + topLeft) / BinaryMaskMaxValue);
        }

        public readonly record struct WhiteContour(double Area, Rect Bounds);
    }
}
