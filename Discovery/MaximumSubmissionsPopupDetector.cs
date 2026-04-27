using OpenCvSharp;

namespace Discovery;

internal sealed class MaximumSubmissionsPopupDetector
{
    private const double SearchLeftRatio = 0.56;
    private const double SearchTopRatio = 0.62;
    private const double SearchRightRatio = 0.92;
    private const double SearchBottomRatio = 0.91;
    private const int TitleWhiteMinimum = 2_500;
    private const int BodyWhiteMinimum = 2_200;
    private const int IconWhiteMinimum = 600;
    private const int ButtonCyanMinimum = 700;
    private const int ButtonWhiteMinimum = 50;
    private const int MinimumSearchDimension = 1;
    private const string DebugOverlayText = "Maximum submissions popup detected";
    private const double DebugOverlayTextScale = 0.8;
    private const int DebugOverlayTextThickness = 2;
    private const int DebugOverlayLeftPadding = 30;
    private const int DebugOverlayTopPadding = 40;
    private static readonly Scalar WhiteMinimum = new(180, 180, 180);
    private static readonly Scalar WhiteMaximum = new(255, 255, 255);
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

        var searchBounds = BuildSearchBounds(image.Size());
        using var searchRegion = new Mat(image, searchBounds);
        var buttonBounds = BuildButtonBounds(searchRegion.Size());
        var buttonEvidenceFound = CountCyanPixels(searchRegion, buttonBounds) >= ButtonCyanMinimum ||
                                  CountWhitePixels(searchRegion, buttonBounds) >= ButtonWhiteMinimum;

        return CountWhitePixels(searchRegion, BuildTitleBounds(searchRegion.Size())) >= TitleWhiteMinimum &&
               CountWhitePixels(searchRegion, BuildBodyBounds(searchRegion.Size())) >= BodyWhiteMinimum &&
               CountWhitePixels(searchRegion, BuildIconBounds(searchRegion.Size())) >= IconWhiteMinimum &&
               buttonEvidenceFound;
    }

    private static Rect BuildSearchBounds(Size imageSize)
    {
        var left = (int)Math.Round(imageSize.Width * SearchLeftRatio);
        var top = (int)Math.Round(imageSize.Height * SearchTopRatio);
        var right = (int)Math.Round(imageSize.Width * SearchRightRatio);
        var bottom = (int)Math.Round(imageSize.Height * SearchBottomRatio);
        return BuildClampedBounds(left, top, right - left, bottom - top, imageSize);
    }

    private static Rect BuildTitleBounds(Size searchSize)
    {
        return BuildClampedBounds(
            (int)Math.Round(searchSize.Width * 0.18),
            0,
            (int)Math.Round(searchSize.Width * 0.73),
            (int)Math.Round(searchSize.Height * 0.28),
            searchSize);
    }

    private static Rect BuildBodyBounds(Size searchSize)
    {
        return BuildClampedBounds(
            (int)Math.Round(searchSize.Width * 0.04),
            (int)Math.Round(searchSize.Height * 0.30),
            (int)Math.Round(searchSize.Width * 0.90),
            (int)Math.Round(searchSize.Height * 0.40),
            searchSize);
    }

    private static Rect BuildIconBounds(Size searchSize)
    {
        return BuildClampedBounds(
            0,
            (int)Math.Round(searchSize.Height * 0.10),
            (int)Math.Round(searchSize.Width * 0.18),
            (int)Math.Round(searchSize.Height * 0.22),
            searchSize);
    }

    private static Rect BuildButtonBounds(Size searchSize)
    {
        return BuildClampedBounds(
            (int)Math.Round(searchSize.Width * 0.04),
            (int)Math.Round(searchSize.Height * 0.68),
            (int)Math.Round(searchSize.Width * 0.90),
            (int)Math.Round(searchSize.Height * 0.24),
            searchSize);
    }

    private static Rect BuildClampedBounds(int x, int y, int width, int height, Size containingSize)
    {
        var clampedX = Math.Clamp(x, 0, Math.Max(0, containingSize.Width - MinimumSearchDimension));
        var clampedY = Math.Clamp(y, 0, Math.Max(0, containingSize.Height - MinimumSearchDimension));
        var clampedWidth = Math.Clamp(width, MinimumSearchDimension, containingSize.Width - clampedX);
        var clampedHeight = Math.Clamp(height, MinimumSearchDimension, containingSize.Height - clampedY);
        return new Rect(clampedX, clampedY, clampedWidth, clampedHeight);
    }

    private static int CountWhitePixels(Mat image, Rect bounds)
    {
        using var region = new Mat(image, bounds);
        using var mask = new Mat();
        Cv2.InRange(region, WhiteMinimum, WhiteMaximum, mask);
        return Cv2.CountNonZero(mask);
    }

    private static int CountCyanPixels(Mat image, Rect bounds)
    {
        using var region = new Mat(image, bounds);
        using var hsv = new Mat();
        using var mask = new Mat();
        Cv2.CvtColor(region, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, CyanMinimum, CyanMaximum, mask);
        return Cv2.CountNonZero(mask);
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
}
