using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;

namespace Automaton;

internal sealed class AsteroidBeltOverviewDetector
{
    private const double MinimumButtonMatchScore = 0.90;
    private const int MinimumAsteroidIconPartArea = 5;
    private const int MaximumAsteroidIconPartWidth = 20;
    private const int MaximumAsteroidIconPartHeight = 20;
    private const int MinimumAsteroidIconPartCount = 2;
    private const int AsteroidIconGroupMaximumDistance = 18;
    private static readonly double[] TemplateScales = [1.0, 0.95, 1.05];

    private readonly Mat m_OverviewBeltTemplate;

    public AsteroidBeltOverviewDetector()
    {
        m_OverviewBeltTemplate = LoadTemplate(Properties.Resources.overview_belt, "overview_belt");
    }

    public AsteroidBeltOverviewAnalysis Analyze(Mat screen)
    {
        if (screen.Empty())
        {
            return AsteroidBeltOverviewAnalysis.NotFound;
        }

        using var searchableScreen = BuildSearchableScreen(screen);
        var overviewSearchBounds = BuildOverviewSearchBounds(searchableScreen.Size());
        var overviewBeltButtonBounds = TryLocateTemplate(
            searchableScreen,
            m_OverviewBeltTemplate,
            overviewSearchBounds,
            out var overviewBeltButtonLocation)
            ? overviewBeltButtonLocation.Bounds
            : (Rect?)null;
        Rect? overviewBounds = overviewBeltButtonBounds is null
            ? (Rect?)null
            : BuildOverviewBounds(searchableScreen.Size(), overviewBeltButtonBounds.Value);
        var asteroidBelts = overviewBounds is null ||
                            overviewBeltButtonBounds is null
            ? []
            : LocateAsteroidBelts(searchableScreen, overviewBounds.Value, overviewBeltButtonBounds.Value);

        return new AsteroidBeltOverviewAnalysis(
            overviewBounds is not null,
            overviewBounds,
            overviewBeltButtonBounds,
            asteroidBelts);
    }

    private static IReadOnlyList<AsteroidBeltOverviewEntry> LocateAsteroidBelts(
        Mat screen,
        Rect overviewBounds,
        Rect overviewBeltButtonBounds)
    {
        var iconColumnBounds = BuildAsteroidIconColumnBounds(screen.Size(), overviewBounds, overviewBeltButtonBounds);
        using var iconColumn = new Mat(screen, iconColumnBounds);
        using var gray = new Mat();
        using var mask = new Mat();
        Cv2.CvtColor(iconColumn, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.InRange(gray, new Scalar(90), new Scalar(180), mask);
        Cv2.FindContours(
            mask,
            out var contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var iconPartCenters = new List<int>();
        foreach (var contour in contours)
        {
            var bounds = Cv2.BoundingRect(contour);
            if (Cv2.ContourArea(contour) < MinimumAsteroidIconPartArea ||
                bounds.Width > MaximumAsteroidIconPartWidth ||
                bounds.Height > MaximumAsteroidIconPartHeight)
            {
                continue;
            }

            iconPartCenters.Add(iconColumnBounds.Y + bounds.Y + bounds.Height / 2);
        }

        var groups = GroupIconPartCenters(iconPartCenters);
        var rowLeft = Math.Clamp(overviewBounds.X + 25, 0, screen.Width - 1);
        var rowWidth = Math.Clamp(overviewBounds.Width - 55, 1, screen.Width - rowLeft);
        var rows = new List<AsteroidBeltOverviewEntry>();
        foreach (var group in groups)
        {
            if (group.Count < MinimumAsteroidIconPartCount)
            {
                continue;
            }

            var centerY = (int)Math.Round(group.Average());
            var rowTop = Math.Clamp(centerY - 17, 0, Math.Max(0, screen.Height - 1));
            var rowHeight = Math.Clamp(34, 1, screen.Height - rowTop);
            rows.Add(new AsteroidBeltOverviewEntry(new Rect(rowLeft, rowTop, rowWidth, rowHeight)));
        }

        return rows
            .OrderBy(row => row.Bounds.Y)
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<int>> GroupIconPartCenters(IReadOnlyList<int> iconPartCenters)
    {
        var groups = new List<List<int>>();
        foreach (var center in iconPartCenters.Order())
        {
            var currentGroup = groups.LastOrDefault();
            if (currentGroup is null ||
                Math.Abs(center - currentGroup.Average()) > AsteroidIconGroupMaximumDistance)
            {
                groups.Add([center]);
                continue;
            }

            currentGroup.Add(center);
        }

        return groups;
    }

    private static bool TryLocateTemplate(
        Mat screen,
        Mat template,
        Rect searchBounds,
        out TemplateLocation location)
    {
        location = default;
        using var searchRegion = new Mat(screen, searchBounds);
        TemplateLocation? bestLocation = null;
        foreach (var scale in TemplateScales)
        {
            using var scaledTemplate = BuildScaledTemplate(template, scale);
            if (scaledTemplate.Width > searchRegion.Width ||
                scaledTemplate.Height > searchRegion.Height)
            {
                continue;
            }

            using var result = new Mat();
            Cv2.MatchTemplate(searchRegion, scaledTemplate, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var score, out _, out var locationPoint);
            var bounds = new Rect(
                searchBounds.X + locationPoint.X,
                searchBounds.Y + locationPoint.Y,
                scaledTemplate.Width,
                scaledTemplate.Height);
            if (bestLocation is null || score > bestLocation.Value.Score)
            {
                bestLocation = new TemplateLocation(bounds, score);
            }
        }

        if (bestLocation is null || bestLocation.Value.Score < MinimumButtonMatchScore)
        {
            return false;
        }

        location = bestLocation.Value;
        return true;
    }

    private static Rect BuildOverviewSearchBounds(Size imageSize)
    {
        var left = (int)Math.Round(imageSize.Width * 0.70);
        var top = (int)Math.Round(imageSize.Height * 0.08);
        return new Rect(
            left,
            top,
            Math.Max(1, imageSize.Width - left),
            Math.Max(1, (int)Math.Round(imageSize.Height * 0.55)));
    }

    private static Rect BuildOverviewBounds(Size imageSize, Rect overviewBeltButtonBounds)
    {
        var left = Math.Clamp(overviewBeltButtonBounds.X - 300, 0, Math.Max(0, imageSize.Width - 1));
        var top = Math.Clamp(overviewBeltButtonBounds.Y - 72, 0, Math.Max(0, imageSize.Height - 1));
        var right = imageSize.Width;
        var bottom = Math.Min(imageSize.Height, top + 900);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static Rect BuildAsteroidIconColumnBounds(
        Size imageSize,
        Rect overviewBounds,
        Rect overviewBeltButtonBounds)
    {
        var left = Math.Clamp(overviewBounds.X + 28, 0, Math.Max(0, imageSize.Width - 1));
        var top = Math.Clamp(overviewBeltButtonBounds.Bottom + 95, 0, Math.Max(0, imageSize.Height - 1));
        var bottom = Math.Min(imageSize.Height, overviewBounds.Bottom);
        var width = Math.Clamp(60, 1, imageSize.Width - left);
        var height = Math.Clamp(bottom - top, 1, imageSize.Height - top);
        return new Rect(left, top, width, height);
    }

    private static Mat BuildSearchableScreen(Mat screen)
    {
        if (screen.Channels() == 3)
        {
            return screen.Clone();
        }

        var colorScreen = new Mat();
        Cv2.CvtColor(screen, colorScreen, ColorConversionCodes.GRAY2BGR);
        return colorScreen;
    }

    private static Mat BuildScaledTemplate(Mat template, double scale)
    {
        if (Math.Abs(scale - 1.0) < double.Epsilon)
        {
            return template.Clone();
        }

        var width = Math.Max(1, (int)Math.Round(template.Width * scale));
        var height = Math.Max(1, (int)Math.Round(template.Height * scale));
        var scaledTemplate = new Mat();
        Cv2.Resize(template, scaledTemplate, new Size(width, height));
        return scaledTemplate;
    }

    private static Mat LoadTemplate(System.Drawing.Bitmap bitmap, string resourceName)
    {
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        var template = Cv2.ImDecode(memoryStream.ToArray(), ImreadModes.Color);
        if (template.Empty())
        {
            throw new InvalidOperationException($"Could not load {resourceName} template from resources.");
        }

        return template;
    }

    private readonly record struct TemplateLocation(Rect Bounds, double Score);
}

internal sealed record AsteroidBeltOverviewAnalysis(
    bool OverviewLocated,
    Rect? OverviewBounds,
    Rect? OverviewBeltButtonBounds,
    IReadOnlyList<AsteroidBeltOverviewEntry> AsteroidBelts)
{
    public static AsteroidBeltOverviewAnalysis NotFound { get; } = new(
        false,
        null,
        null,
        []);
}

internal sealed record AsteroidBeltOverviewEntry(Rect Bounds);
