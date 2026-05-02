using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;

namespace Automaton;

internal sealed class PlayNowButtonLocator
{
    private const double MinimumMatchScore = 0.86;
    private static readonly double[] TemplateScales = [1.0, 0.95, 1.05, 0.90, 1.10];

    private readonly Mat m_Template;

    public PlayNowButtonLocator()
    {
        m_Template = LoadPlayButtonFromResources();
        if (m_Template.Empty())
        {
            throw new InvalidOperationException("Could not load PLAY NOW template from Properties.Resources.play.");
        }
    }

    public bool TryLocate(Mat screen, out PlayNowButtonLocation location)
    {
        location = default;
        if (screen.Empty())
        {
            return false;
        }

        using var searchableScreen = BuildSearchableScreen(screen);
        PlayNowButtonLocation? bestLocation = null;
        foreach (var scale in TemplateScales)
        {
            using var scaledTemplate = BuildScaledTemplate(scale);
            if (scaledTemplate.Width > searchableScreen.Width || scaledTemplate.Height > searchableScreen.Height)
            {
                continue;
            }

            using var result = new Mat();
            Cv2.MatchTemplate(searchableScreen, scaledTemplate, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var score, out _, out var locationPoint);
            var bounds = new Rect(locationPoint.X, locationPoint.Y, scaledTemplate.Width, scaledTemplate.Height);
            if (bestLocation is null || score > bestLocation.Value.Score)
            {
                bestLocation = new PlayNowButtonLocation(bounds, score);
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

    private static Mat LoadPlayButtonFromResources()
    {
        using var bitmap = Properties.Resources.play;
        if (bitmap is null)
        {
            throw new InvalidOperationException("Properties.Resources.play returned null.");
        }

        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        return Cv2.ImDecode(memoryStream.ToArray(), ImreadModes.Color);
    }
}

internal readonly record struct PlayNowButtonLocation(Rect Bounds, double Score);
