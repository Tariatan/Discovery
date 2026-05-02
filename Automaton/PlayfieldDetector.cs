using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;

namespace Automaton;

internal sealed class PlayfieldDetector
{
    private const int EdgeLowThreshold = 60;
    private const int EdgeHighThreshold = 160;
    private const int StrictPassMaxMatches = 8;
    private const int AdaptivePassMaxMatches = 12;
    private const int MaxRetainedCandidates = 18;
    private const int MinimumMarkersForPlayfield = 3;
    private const double RawMatchThreshold = 0.68;
    private const double EqualizedMatchThreshold = 0.62;
    private const double EdgeMatchThreshold = 0.58;
    private const double AdaptiveRawMatchThreshold = 0.52;
    private const double AdaptiveEdgeMatchThreshold = 0.42;
    private const double RawMatchWeight = 1.00;
    private const double EqualizedMatchWeight = 0.97;
    private const double EdgeMatchWeight = 0.94;
    private const double AdaptiveRawMatchWeight = 0.90;
    private const double AdaptiveEdgeMatchWeight = 0.88;
    private const double CandidateOverlapThreshold = 0.35;
    private const double SuppressionSizeScale = 0.8;
    private const int MarkerScoreWeight = 1_000;
    private const int InferredCornerPenalty = 40_000;
    private const int MinimumPlayfieldDimension = 450;
    private const int MinimumMarkerDimension = 1;
    private const double MinimumPlayfieldAspectRatio = 0.65;
    private const double MaximumPlayfieldAspectRatio = 1.60;
    private const int MaximumAlignmentDelta = 65;
    private const int MaximumSpanDelta = 120;
    private const int ScoreAlignmentPenaltyWeight = 500;

    private static readonly Corner[] AllCorners = [Corner.TopLeft, Corner.TopRight, Corner.BottomLeft, Corner.BottomRight];

    private readonly Mat m_TemplateGray;
    private readonly Mat m_TemplateEqualized;
    private readonly Mat m_TemplateEdges;
    private readonly Size m_TemplateSize;

    public PlayfieldDetector()
    {
        m_TemplateGray = LoadMarkerFromResources();
        if (m_TemplateGray.Empty())
        {
            throw new InvalidOperationException("Could not load playfield marker template from Properties.Resources.marker.");
        }

        m_TemplateEqualized = new Mat();
        Cv2.EqualizeHist(m_TemplateGray, m_TemplateEqualized);
        m_TemplateEdges = BuildEdgeMap(m_TemplateEqualized);
        m_TemplateSize = m_TemplateGray.Size();
    }

    public PlayfieldDetectionResult Detect(Mat screenshot)
    {
        using var screenshotGray = new Mat();
        using var screenshotEqualized = new Mat();
        using var screenshotEdges = new Mat();

        // Normalize the screenshot into multiple representations so template matching
        // can still succeed when one corner is dimmed, saturated, or partially occluded.
        Cv2.CvtColor(screenshot, screenshotGray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(screenshotGray, screenshotEqualized);
        Cv2.Canny(screenshotEqualized, screenshotEdges, EdgeLowThreshold, EdgeHighThreshold);

        var markerCandidates = FindMarkerCandidates(screenshotGray, screenshotEqualized, screenshotEdges);
        var markerSet = SelectMarkerSet(markerCandidates, screenshot.Size());

        if (markerSet is null)
        {
            return PlayfieldDetectionResult.NotFound;
        }

        var bounds = BuildPlayfieldBounds(markerSet.Value);
        return new PlayfieldDetectionResult(bounds, markerSet.Value.AllMarkers);
    }

    private List<MarkerCandidate> FindMarkerCandidates(Mat screenshotGray, Mat screenshotEqualized, Mat screenshotEdges)
    {
        var candidates = new List<MarkerCandidate>();

        // Start strict, then gradually widen the search and add edge-based matching when needed.
        CollectMatchCandidates(screenshotGray, m_TemplateGray, RawMatchThreshold, RawMatchWeight, StrictPassMaxMatches, candidates);

        if (candidates.Count < 4)
        {
            CollectMatchCandidates(screenshotEqualized, m_TemplateEqualized, EqualizedMatchThreshold, EqualizedMatchWeight, StrictPassMaxMatches, candidates);
        }

        if (candidates.Count < 4)
        {
            CollectMatchCandidates(screenshotEdges, m_TemplateEdges, EdgeMatchThreshold, EdgeMatchWeight, StrictPassMaxMatches, candidates);
        }

        if (candidates.Count < 4)
        {
            CollectMatchCandidates(screenshotGray, m_TemplateGray, AdaptiveRawMatchThreshold, AdaptiveRawMatchWeight, AdaptivePassMaxMatches, candidates);
            CollectMatchCandidates(screenshotEdges, m_TemplateEdges, AdaptiveEdgeMatchThreshold, AdaptiveEdgeMatchWeight, AdaptivePassMaxMatches, candidates);
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .Take(MaxRetainedCandidates)
            .ToList();
    }

    private void CollectMatchCandidates(
        Mat screenshot,
        Mat template,
        double threshold,
        double scoreWeight,
        int maxMatches,
        List<MarkerCandidate> candidates)
    {
        using var result = new Mat();
        Cv2.MatchTemplate(screenshot, template, result, TemplateMatchModes.CCoeffNormed);

        for (var matchIndex = 0; matchIndex < maxMatches; matchIndex++)
        {
            Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);
            if (maxValue < threshold)
            {
                break;
            }

            var candidate = new MarkerCandidate(
                new Rect(maxLocation.X, maxLocation.Y, m_TemplateSize.Width, m_TemplateSize.Height),
                maxValue * scoreWeight);

            AddOrMergeCandidate(candidates, candidate);
            SuppressNeighborhood(result, maxLocation, m_TemplateSize);
        }
    }

    private static void AddOrMergeCandidate(List<MarkerCandidate> candidates, MarkerCandidate candidate)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            if (!Overlaps(candidate.Bounds, candidates[index].Bounds))
            {
                continue;
            }

            if (candidate.Score > candidates[index].Score)
            {
                candidates[index] = candidate;
            }

            return;
        }

        candidates.Add(candidate);
    }

    private static bool Overlaps(Rect left, Rect right)
    {
        var intersection = left & right;
        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return false;
        }

        var overlapArea = intersection.Width * intersection.Height;
        var minimumArea = Math.Min(left.Width * left.Height, right.Width * right.Height);
        return overlapArea >= minimumArea * CandidateOverlapThreshold;
    }

    private static void SuppressNeighborhood(Mat result, Point location, Size templateSize)
    {
        var suppressionWidth = (int)Math.Round(templateSize.Width * SuppressionSizeScale);
        var suppressionHeight = (int)Math.Round(templateSize.Height * SuppressionSizeScale);

        var x = Math.Max(0, location.X - suppressionWidth / 2);
        var y = Math.Max(0, location.Y - suppressionHeight / 2);
        var width = Math.Min(result.Width - x, suppressionWidth);
        var height = Math.Min(result.Height - y, suppressionHeight);

        using var roi = new Mat(result, new Rect(x, y, width, height));
        roi.SetTo(new Scalar(0));
    }

    private static MarkerSet? SelectMarkerSet(IReadOnlyList<MarkerCandidate> candidates, Size imageSize)
    {
        if (candidates.Count < MinimumMarkersForPlayfield)
        {
            return null;
        }

        MarkerSet? best = null;
        var bestScore = double.NegativeInfinity;

        // Evaluate every plausible 3-marker or 4-marker combination and keep the
        // most consistent rectangle, preferring fully observed corners when possible.
        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                for (var k = j + 1; k < candidates.Count; k++)
                {
                    EvaluateCombination([candidates[i], candidates[j], candidates[k]], imageSize, ref best, ref bestScore);

                    for (var m = k + 1; m < candidates.Count; m++)
                    {
                        EvaluateCombination([candidates[i], candidates[j], candidates[k], candidates[m]], imageSize, ref best, ref bestScore);
                    }
                }
            }
        }

        return best;
    }

    private static void EvaluateCombination(
        IReadOnlyList<MarkerCandidate> combination,
        Size imageSize,
        ref MarkerSet? best,
        ref double bestScore)
    {
        if (!TryBuildMarkerSet(combination, imageSize, out var set))
        {
            return;
        }

        var score = Score(set) + combination.Sum(candidate => candidate.Score) * MarkerScoreWeight - (set.InferredCorner is null ? 0 : InferredCornerPenalty);
        if (score <= bestScore)
        {
            return;
        }

        best = set;
        bestScore = score;
    }

    private static bool TryBuildMarkerSet(IReadOnlyList<MarkerCandidate> markers, Size imageSize, out MarkerSet set)
    {
        var candidateSet = markers.Count switch
        {
            4 => TryBuildMarkerSetFromFour(markers.Select(marker => marker.Bounds).ToArray()),
            3 => TryBuildMarkerSetFromThree(markers.Select(marker => marker.Bounds).ToArray()),
            _ => null
        };

        if (candidateSet is null || !IsValid(candidateSet.Value, imageSize))
        {
            set = default;
            return false;
        }

        set = candidateSet.Value;
        return true;
    }

    private static MarkerSet? TryBuildMarkerSetFromFour(IReadOnlyList<Rect> markers)
    {
        var orderedByY = markers.OrderBy(CenterY).ToArray();
        var topRow = orderedByY.Take(2).OrderBy(rect => rect.X).ToArray();
        var bottomRow = orderedByY.Skip(2).OrderBy(rect => rect.X).ToArray();

        return new MarkerSet(topRow[0], topRow[1], bottomRow[0], bottomRow[1], null);
    }

    private static MarkerSet? TryBuildMarkerSetFromThree(IReadOnlyList<Rect> markers)
    {
        var leftX = markers.Min(rect => rect.X);
        var rightX = markers.Max(rect => rect.X);
        var topY = markers.Min(rect => rect.Y);
        var bottomY = markers.Max(rect => rect.Y);
        var averageWidth = Math.Max(MinimumMarkerDimension, (int)Math.Round(markers.Average(rect => rect.Width)));
        var averageHeight = Math.Max(MinimumMarkerDimension, (int)Math.Round(markers.Average(rect => rect.Height)));

        var cornerMap = new Dictionary<Corner, Rect>();

        if ((from marker
                    in markers
                let corner = ClassifyCorner(marker, leftX, rightX, topY, bottomY)
                where !cornerMap.TryAdd(corner, marker)
                select marker).Any())
        {
            return null;
        }

        if (cornerMap.Count != 3)
        {
            return null;
        }

        var inferredCorner = AllCorners.Single(corner => !cornerMap.ContainsKey(corner));
        cornerMap[inferredCorner] = inferredCorner switch
        {
            Corner.TopLeft => new Rect(leftX, topY, averageWidth, averageHeight),
            Corner.TopRight => new Rect(rightX, topY, averageWidth, averageHeight),
            Corner.BottomLeft => new Rect(leftX, bottomY, averageWidth, averageHeight),
            Corner.BottomRight => new Rect(rightX, bottomY, averageWidth, averageHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(inferredCorner), inferredCorner, null)
        };

        return new MarkerSet(
            cornerMap[Corner.TopLeft],
            cornerMap[Corner.TopRight],
            cornerMap[Corner.BottomLeft],
            cornerMap[Corner.BottomRight],
            inferredCorner);
    }

    private static Corner ClassifyCorner(Rect marker, int leftX, int rightX, int topY, int bottomY)
    {
        var isLeft = Math.Abs(marker.X - leftX) <= Math.Abs(marker.X - rightX);
        var isTop = Math.Abs(marker.Y - topY) <= Math.Abs(marker.Y - bottomY);

        return (isLeft, isTop) switch
        {
            (true, true) => Corner.TopLeft,
            (false, true) => Corner.TopRight,
            (true, false) => Corner.BottomLeft,
            _ => Corner.BottomRight
        };
    }

    private static bool IsValid(MarkerSet set, Size imageSize)
    {
        var playfield = BuildPlayfieldBounds(set);
        if (playfield.Width < MinimumPlayfieldDimension || playfield.Height < MinimumPlayfieldDimension)
        {
            return false;
        }

        if (playfield.X < 0 || playfield.Y < 0 || playfield.Right > imageSize.Width || playfield.Bottom > imageSize.Height)
        {
            return false;
        }

        var aspectRatio = playfield.Width / (double)playfield.Height;
        if (aspectRatio is < MinimumPlayfieldAspectRatio or > MaximumPlayfieldAspectRatio)
        {
            return false;
        }

        var topAlignment = Math.Abs(CenterY(set.TopLeft) - CenterY(set.TopRight));
        var bottomAlignment = Math.Abs(CenterY(set.BottomLeft) - CenterY(set.BottomRight));
        var leftAlignment = Math.Abs(CenterX(set.TopLeft) - CenterX(set.BottomLeft));
        var rightAlignment = Math.Abs(CenterX(set.TopRight) - CenterX(set.BottomRight));

        if (topAlignment > MaximumAlignmentDelta || bottomAlignment > MaximumAlignmentDelta || leftAlignment > MaximumAlignmentDelta || rightAlignment > MaximumAlignmentDelta)
        {
            return false;
        }

        var topSpan = (set.TopRight.X + set.TopRight.Width) - set.TopLeft.X;
        var bottomSpan = (set.BottomRight.X + set.BottomRight.Width) - set.BottomLeft.X;
        var leftSpan = (set.BottomLeft.Y + set.BottomLeft.Height) - set.TopLeft.Y;
        var rightSpan = (set.BottomRight.Y + set.BottomRight.Height) - set.TopRight.Y;

        return Math.Abs(topSpan - bottomSpan) <= MaximumSpanDelta && Math.Abs(leftSpan - rightSpan) <= MaximumSpanDelta;
    }

    private static Rect BuildPlayfieldBounds(MarkerSet set)
    {
        var left = set.TopLeft.X;
        var top = set.TopLeft.Y;
        var right = set.TopRight.X + set.TopRight.Width;
        var bottom = set.BottomLeft.Y + set.BottomLeft.Height;
        return new Rect(left, top, Math.Max(MinimumMarkerDimension, right - left), Math.Max(MinimumMarkerDimension, bottom - top));
    }

    private static double Score(MarkerSet set)
    {
        var bounds = BuildPlayfieldBounds(set);
        var alignmentPenalty =
            Math.Abs(CenterY(set.TopLeft) - CenterY(set.TopRight)) +
            Math.Abs(CenterY(set.BottomLeft) - CenterY(set.BottomRight)) +
            Math.Abs(CenterX(set.TopLeft) - CenterX(set.BottomLeft)) +
            Math.Abs(CenterX(set.TopRight) - CenterX(set.BottomRight));

        return (bounds.Width * bounds.Height) - (alignmentPenalty * ScoreAlignmentPenaltyWeight);
    }

    private static double CenterX(Rect rect) => rect.X + rect.Width / 2.0;

    private static double CenterY(Rect rect) => rect.Y + rect.Height / 2.0;

    private static Mat LoadMarkerFromResources()
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

    private static Mat BuildEdgeMap(Mat input)
    {
        var edges = new Mat();
        Cv2.Canny(input, edges, EdgeLowThreshold, EdgeHighThreshold);
        return edges;
    }

    private readonly record struct MarkerCandidate(Rect Bounds, double Score);

    private readonly record struct MarkerSet(
        Rect TopLeft,
        Rect TopRight,
        Rect BottomLeft,
        Rect BottomRight,
        Corner? InferredCorner)
    {
        public Rect[] AllMarkers => [TopLeft, TopRight, BottomLeft, BottomRight];
    }

    private enum Corner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}

internal sealed record PlayfieldDetectionResult(Rect Bounds, IReadOnlyList<Rect> MarkerBounds)
{
    public static PlayfieldDetectionResult NotFound { get; } = new(new Rect(), Array.Empty<Rect>());

    public bool IsFound => Bounds is { Width: > 0, Height: > 0 };
}
