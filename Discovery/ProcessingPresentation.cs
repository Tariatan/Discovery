using System.IO;

namespace Discovery;

internal static class ProjectRootLocator
{
    public static string ResolveFromBaseDirectory(string baseDirectory)
    {
        var directory = new DirectoryInfo(baseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Discovery.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the project root from the application base directory.");
    }
}

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
