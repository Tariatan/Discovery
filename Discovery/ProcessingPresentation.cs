using System.IO;

namespace Discovery;

internal static class SampleProcessingSummaryFormatter
{
    public static string BuildSummaryText(SampleProcessingSummary summary)
    {
        var lines = new List<string>
        {
            $"Samples folder: {summary.SamplesDirectory}",
            $"Debug output:  {summary.OutputDirectory}",
            string.Empty
        };

        foreach (var result in summary.Results)
        {
            lines.Add($"{result.FileName,-12} playfield={(result.PlayfieldFound ? "yes" : "no"),-3}  clusters={result.ClusterCount}  output={result.OutputPath}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

internal static class ScreenCaptureSummaryFormatter
{
    public static string BuildSummaryText(ScreenCaptureSummary summary)
    {
        var lines = new List<string>
        {
            $"Captures folder: {summary.CapturesDirectory}",
            $"Debug output:   {summary.OutputDirectory}",
            $"Capture image:  {summary.CapturePath}",
            string.Empty,
            $"{summary.Result.FileName,-20} playfield={(summary.Result.PlayfieldFound ? "yes" : "no"),-3}  clusters={summary.Result.ClusterCount}  output={summary.Result.OutputPath}"
        };

        return string.Join(Environment.NewLine, lines);
    }
}

internal static class AutomationSummaryFormatter
{
    public static string BuildSummaryText(AutomationSummary summary)
    {
        var controlButtonText = summary.ControlButtonBounds is null
            ? "not found"
            : $"{summary.ControlButtonBounds.Value.X},{summary.ControlButtonBounds.Value.Y} {summary.ControlButtonBounds.Value.Width}x{summary.ControlButtonBounds.Value.Height}";

        var lines = new List<string>
        {
            $"Capture image:      {summary.CaptureSummary.CapturePath}",
            $"Annotated output:   {summary.CaptureSummary.Analysis.Result.OutputPath}",
            $"Playfield found:    {(summary.CaptureSummary.Analysis.Result.PlayfieldFound ? "yes" : "no")}",
            $"Clusters detected:  {summary.CaptureSummary.Analysis.Result.ClusterCount}",
            $"Polygon clicks:     {summary.ClickedPointCount}",
            $"Control button:     {controlButtonText}",
            string.Empty,
            "Control was focused but not clicked."
        };

        return string.Join(Environment.NewLine, lines);
    }
}
