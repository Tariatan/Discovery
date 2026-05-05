using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;

namespace Automaton;

internal sealed class LocationChangeTimerLocator
{
    private const double MinimumMatchScore = 0.90;
    private static readonly Rect SearchBounds = new(80, 20, 180, 130);
    private static readonly double[] TemplateScales = [1.0, 0.95, 1.05];

    private readonly Mat m_Template;

    public LocationChangeTimerLocator()
    {
        m_Template = LoadLocationChangeTimerFromResources();
        if (m_Template.Empty())
        {
            throw new InvalidOperationException("Could not load location change timer template from resources.");
        }
    }

    public bool TryLocate(Mat screen, out LocationChangeTimerLocation location)
    {
        location = default;
        if (screen.Empty())
        {
            return false;
        }

        var searchBounds = BuildSearchBounds(screen.Size());
        using var searchableScreen = BuildSearchableScreen(screen);
        using var searchRegion = new Mat(searchableScreen, searchBounds);
        LocationChangeTimerLocation? bestLocation = null;
        foreach (var scale in TemplateScales)
        {
            using var scaledTemplate = BuildScaledTemplate(scale);
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
                bestLocation = new LocationChangeTimerLocation(bounds, score);
            }
        }

        if (bestLocation is null || bestLocation.Value.Score < MinimumMatchScore)
        {
            return false;
        }

        location = bestLocation.Value;
        return true;
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

    private static Rect BuildSearchBounds(Size imageSize)
    {
        var x = Math.Clamp(SearchBounds.X, 0, Math.Max(0, imageSize.Width - 1));
        var y = Math.Clamp(SearchBounds.Y, 0, Math.Max(0, imageSize.Height - 1));
        var width = Math.Clamp(SearchBounds.Width, 1, imageSize.Width - x);
        var height = Math.Clamp(SearchBounds.Height, 1, imageSize.Height - y);
        return new Rect(x, y, width, height);
    }

    private Mat BuildScaledTemplate(double scale)
    {
        if (Math.Abs(scale - 1.0) < double.Epsilon)
        {
            return m_Template.Clone();
        }

        var width = Math.Max(1, (int)Math.Round(m_Template.Width * scale));
        var height = Math.Max(1, (int)Math.Round(m_Template.Height * scale));
        var scaledTemplate = new Mat();
        Cv2.Resize(m_Template, scaledTemplate, new Size(width, height));
        return scaledTemplate;
    }

    private static Mat LoadLocationChangeTimerFromResources()
    {
        using var bitmap = Properties.Resources.location_change_timer;
        if (bitmap is null)
        {
            throw new InvalidOperationException("Properties.Resources.location_change_timer returned null.");
        }

        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        return Cv2.ImDecode(memoryStream.ToArray(), ImreadModes.Color);
    }
}

internal readonly record struct LocationChangeTimerLocation(Rect Bounds, double Score);
