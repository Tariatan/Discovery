using System.IO;
using OpenCvSharp;

namespace Discovery;

internal sealed class PilotAvatarLocator
{
    private const string PilotFolderName = "pilot";
    private const double MinimumMatchScore = 0.90;
    private static readonly double[] TemplateScales = [1.0, 0.95, 1.05, 0.90, 1.10];

    public int GetNextPilotIndex(int currentPilotIndex)
    {
        return TryGetNextPilotIndex(currentPilotIndex, out var nextPilotIndex)
            ? nextPilotIndex
            : GetFirstPilotIndex();
    }

    public bool TryGetNextPilotIndex(int currentPilotIndex, out int nextPilotIndex)
    {
        var availablePilotIndices = GetAvailablePilotIndices();
        foreach (var pilotIndex in availablePilotIndices)
        {
            if (pilotIndex > currentPilotIndex)
            {
                nextPilotIndex = pilotIndex;
                return true;
            }
        }

        nextPilotIndex = currentPilotIndex;
        return false;
    }

    public bool TryLocate(Mat screen, int pilotIndex, out PilotAvatarLocation location)
    {
        location = default;
        if (screen.Empty() || !Directory.Exists(PilotFolderName))
        {
            return false;
        }

        PilotAvatarLocation? bestLocation = null;
        foreach (var candidate in BuildCandidates(PilotFolderName, pilotIndex))
        {
            if (!File.Exists(candidate.Path))
            {
                continue;
            }

            if (!TryMatchTemplate(screen, candidate, out var candidateLocation))
            {
                continue;
            }

            if (bestLocation is null || candidateLocation.Score > bestLocation.Value.Score)
            {
                bestLocation = candidateLocation;
            }
        }

        if (bestLocation is null || bestLocation.Value.Score < MinimumMatchScore)
        {
            return false;
        }

        location = bestLocation.Value;
        return true;
    }

    private static IReadOnlyList<int> GetAvailablePilotIndices()
    {
        if (!Directory.Exists(PilotFolderName))
        {
            return Array.Empty<int>();
        }

        return Directory
            .EnumerateFiles(PilotFolderName, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Select(ParsePilotIndex)
            .Where(pilotIndex => pilotIndex > 0)
            .Distinct()
            .Order()
            .ToArray();
    }

    private static int GetFirstPilotIndex()
    {
        var availablePilotIndices = GetAvailablePilotIndices();
        return availablePilotIndices.Count == 0
            ? 1
            : availablePilotIndices[0];
    }

    private static int ParsePilotIndex(string? fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return 0;
        }

        var indexText = fileNameWithoutExtension.EndsWith("_focused", StringComparison.OrdinalIgnoreCase)
            ? fileNameWithoutExtension[..^"_focused".Length]
            : fileNameWithoutExtension;
        return int.TryParse(indexText, out var pilotIndex)
            ? pilotIndex
            : 0;
    }

    private static IReadOnlyList<PilotAvatarCandidate> BuildCandidates(string pilotDirectory, int pilotIndex)
    {
        return
        [
            new PilotAvatarCandidate(Path.Combine(pilotDirectory, $"{pilotIndex}_focused.png"), UsesColor: true),
            new PilotAvatarCandidate(Path.Combine(pilotDirectory, $"{pilotIndex}.png"), UsesColor: false)
        ];
    }

    private static bool TryMatchTemplate(
        Mat screen,
        PilotAvatarCandidate candidate,
        out PilotAvatarLocation location)
    {
        location = default;
        using var searchableScreen = BuildSearchableScreen(screen, candidate.UsesColor);
        using var template = Cv2.ImRead(
            candidate.Path,
            candidate.UsesColor ? ImreadModes.Color : ImreadModes.Grayscale);
        if (searchableScreen.Empty() || template.Empty())
        {
            return false;
        }

        PilotAvatarLocation? bestLocation = null;
        foreach (var scale in TemplateScales)
        {
            using var scaledTemplate = BuildScaledTemplate(template, scale);
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
                bestLocation = new PilotAvatarLocation(bounds, candidate.Path, score);
            }
        }

        if (bestLocation is null)
        {
            return false;
        }

        location = bestLocation.Value;
        return true;
    }

    private static Mat BuildSearchableScreen(Mat screen, bool useColor)
    {
        if (useColor)
        {
            if (screen.Channels() == 3)
            {
                return screen.Clone();
            }

            var colorScreen = new Mat();
            Cv2.CvtColor(screen, colorScreen, ColorConversionCodes.GRAY2BGR);
            return colorScreen;
        }

        if (screen.Channels() == 1)
        {
            return screen.Clone();
        }

        var grayScreen = new Mat();
        Cv2.CvtColor(screen, grayScreen, ColorConversionCodes.BGR2GRAY);
        return grayScreen;
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

    private readonly record struct PilotAvatarCandidate(string Path, bool UsesColor);
}

internal readonly record struct PilotAvatarLocation(
    Rect Bounds,
    string TemplatePath,
    double Score);
