using OpenCvSharp;
using System.IO;
using System.Drawing.Imaging;

namespace Discovery;

internal sealed class PlayfieldDetector
{
    private readonly Mat _templateGray;
    private readonly Size _templateSize;

    public PlayfieldDetector()
    {
        _templateGray = LoadTemplateFromResources();
        if (_templateGray.Empty())
        {
            throw new InvalidOperationException("Could not load playfield marker template from Properties.Resources.marker.");
        }

        _templateSize = _templateGray.Size();
    }

    public PlayfieldDetectionResult Detect(Mat screenshot)
    {
        using var screenshotGray = new Mat();
        Cv2.CvtColor(screenshot, screenshotGray, ColorConversionCodes.BGR2GRAY);

        var markerCandidates = FindMarkerCandidates(screenshotGray);
        var markerSet = SelectMarkerSet(markerCandidates, screenshot.Size());

        if (markerSet is null)
        {
            return PlayfieldDetectionResult.NotFound;
        }

        var bounds = BuildPlayfieldBounds(markerSet.Value);
        return new PlayfieldDetectionResult(bounds, markerSet.Value.AllMarkers);
    }

    private List<Rect> FindMarkerCandidates(Mat screenshotGray)
    {
        using var result = new Mat();
        Cv2.MatchTemplate(screenshotGray, _templateGray, result, TemplateMatchModes.CCoeffNormed);

        var candidates = new List<Rect>();
        const double threshold = 0.68;

        while (true)
        {
            Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);
            if (maxValue < threshold)
            {
                break;
            }

            var candidate = new Rect(maxLocation.X, maxLocation.Y, _templateSize.Width, _templateSize.Height);
            candidates.Add(candidate);

            SuppressNeighborhood(result, maxLocation, _templateSize);
        }

        return candidates;
    }

    private static void SuppressNeighborhood(Mat result, Point location, Size templateSize)
    {
        var suppressionWidth = (int)Math.Round(templateSize.Width * 0.8);
        var suppressionHeight = (int)Math.Round(templateSize.Height * 0.8);

        var x = Math.Max(0, location.X - suppressionWidth / 2);
        var y = Math.Max(0, location.Y - suppressionHeight / 2);
        var width = Math.Min(result.Width - x, suppressionWidth);
        var height = Math.Min(result.Height - y, suppressionHeight);

        using var roi = new Mat(result, new Rect(x, y, width, height));
        roi.SetTo(new Scalar(0));
    }

    private static MarkerSet? SelectMarkerSet(IReadOnlyList<Rect> candidates, Size imageSize)
    {
        if (candidates.Count < 4)
        {
            return null;
        }

        MarkerSet? best = null;
        var bestScore = double.NegativeInfinity;

        for (var i = 0; i < candidates.Count - 3; i++)
        {
            for (var j = i + 1; j < candidates.Count - 2; j++)
            {
                for (var k = j + 1; k < candidates.Count - 1; k++)
                {
                    for (var m = k + 1; m < candidates.Count; m++)
                    {
                        var set = TryBuildMarkerSet([candidates[i], candidates[j], candidates[k], candidates[m]], imageSize);
                        if (set is null)
                        {
                            continue;
                        }

                        var score = Score(set.Value);
                        if (score > bestScore)
                        {
                            best = set;
                            bestScore = score;
                        }
                    }
                }
            }
        }

        return best;
    }

    private static MarkerSet? TryBuildMarkerSet(IReadOnlyList<Rect> markers, Size imageSize)
    {
        var orderedByY = markers.OrderBy(CenterY).ToArray();
        var topRow = orderedByY.Take(2).OrderBy(rect => rect.X).ToArray();
        var bottomRow = orderedByY.Skip(2).OrderBy(rect => rect.X).ToArray();

        var topLeft = topRow[0];
        var topRight = topRow[1];
        var bottomLeft = bottomRow[0];
        var bottomRight = bottomRow[1];

        var playfield = BuildPlayfieldBounds(new MarkerSet(topLeft, topRight, bottomLeft, bottomRight));

        if (playfield.Width < 450 || playfield.Height < 450)
        {
            return null;
        }

        if (playfield.X > imageSize.Width * 0.10 || playfield.Y > imageSize.Height * 0.20)
        {
            return null;
        }

        if (playfield.Right > imageSize.Width * 0.42 || playfield.Bottom > imageSize.Height * 0.68)
        {
            return null;
        }

        var topAlignment = Math.Abs(CenterY(topLeft) - CenterY(topRight));
        var bottomAlignment = Math.Abs(CenterY(bottomLeft) - CenterY(bottomRight));
        var leftAlignment = Math.Abs(CenterX(topLeft) - CenterX(bottomLeft));
        var rightAlignment = Math.Abs(CenterX(topRight) - CenterX(bottomRight));

        if (topAlignment > 35 || bottomAlignment > 35 || leftAlignment > 35 || rightAlignment > 35)
        {
            return null;
        }

        var topSpan = (topRight.X + topRight.Width) - topLeft.X;
        var bottomSpan = (bottomRight.X + bottomRight.Width) - bottomLeft.X;
        var leftSpan = (bottomLeft.Y + bottomLeft.Height) - topLeft.Y;
        var rightSpan = (bottomRight.Y + bottomRight.Height) - topRight.Y;
        if (Math.Abs(topSpan - bottomSpan) > 80 || Math.Abs(leftSpan - rightSpan) > 80)
        {
            return null;
        }

        return new MarkerSet(topLeft, topRight, bottomLeft, bottomRight);
    }

    private static Rect BuildPlayfieldBounds(MarkerSet set)
    {
        var left = set.TopLeft.X;
        var top = set.TopLeft.Y;
        var right = set.TopRight.X + set.TopRight.Width;
        var bottom = set.BottomLeft.Y + set.BottomLeft.Height;
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static double Score(MarkerSet set)
    {
        var bounds = BuildPlayfieldBounds(set);
        var alignmentPenalty =
            Math.Abs(CenterY(set.TopLeft) - CenterY(set.TopRight)) +
            Math.Abs(CenterY(set.BottomLeft) - CenterY(set.BottomRight)) +
            Math.Abs(CenterX(set.TopLeft) - CenterX(set.BottomLeft)) +
            Math.Abs(CenterX(set.TopRight) - CenterX(set.BottomRight));

        return (bounds.Width * bounds.Height) - (alignmentPenalty * 500);
    }

    private static double CenterX(Rect rect) => rect.X + rect.Width / 2.0;

    private static double CenterY(Rect rect) => rect.Y + rect.Height / 2.0;

    private static Mat LoadTemplateFromResources()
    {
        using var bitmap = Properties.Resources.marker;
        if (bitmap is null)
        {
            throw new InvalidOperationException("Properties.Resources.marker returned null.");
        }

        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        return Cv2.ImDecode(memoryStream.ToArray(), ImreadModes.Grayscale);
    }

    private readonly record struct MarkerSet(Rect TopLeft, Rect TopRight, Rect BottomLeft, Rect BottomRight)
    {
        public Rect[] AllMarkers => [TopLeft, TopRight, BottomLeft, BottomRight];
    }
}

internal sealed record PlayfieldDetectionResult(Rect Bounds, IReadOnlyList<Rect> MarkerBounds)
{
    public static PlayfieldDetectionResult NotFound { get; } = new(new Rect(), Array.Empty<Rect>());

    public bool IsFound => Bounds is { Width: > 0, Height: > 0 };
}
