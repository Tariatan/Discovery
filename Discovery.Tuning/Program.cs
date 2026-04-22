using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Discovery;
using OpenCvSharp;

var options = CommandLineOptions.Parse(args);
if (options.ShowHelp)
{
    WriteHelp();
    return 0;
}

return options.Mode switch
{
    RunMode.Evaluate => RunEvaluation(options),
    RunMode.Sweep => RunSweep(options),
    RunMode.ChildEvaluate => RunChildEvaluation(options),
    _ => throw new InvalidOperationException($"Unsupported mode: {options.Mode}")
};

static int RunEvaluation(CommandLineOptions options)
{
    var result = EvaluateConfiguration(options.SamplesDirectory, options.RefinedBoundingArea, options.PolygonBoundingArea);
    WriteEvaluation(result);
    return 0;
}

static int RunSweep(CommandLineOptions options)
{
    var refinedValues = options.RefinedSweepValues.Length > 0
        ? options.RefinedSweepValues
        : [options.RefinedBoundingArea];
    var polygonValues = options.PolygonSweepValues.Length > 0
        ? options.PolygonSweepValues
        : [options.PolygonBoundingArea];

    var evaluations = new List<ConfigurationEvaluation>(refinedValues.Length * polygonValues.Length);
    foreach (var refined in refinedValues)
    {
        foreach (var polygon in polygonValues)
        {
            var evaluation = EvaluateConfiguration(options.SamplesDirectory, refined, polygon);
            evaluations.Add(evaluation);
            Console.WriteLine($"{refined,6} / {polygon,6}  total={evaluation.TotalScore:F6}  avg={evaluation.AverageScore:F6}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Top configurations:");
    foreach (var evaluation in evaluations.OrderByDescending(item => item.TotalScore).ThenByDescending(item => item.AverageScore).Take(10))
    {
        Console.WriteLine($"{evaluation.RefinedBoundingArea,6} / {evaluation.PolygonBoundingArea,6}  total={evaluation.TotalScore:F6}  avg={evaluation.AverageScore:F6}");
    }

    var best = evaluations
        .OrderByDescending(item => item.TotalScore)
        .ThenByDescending(item => item.AverageScore)
        .First();

    Console.WriteLine();
    Console.WriteLine("Best configuration details:");
    WriteEvaluation(best);
    return 0;
}

static ConfigurationEvaluation EvaluateConfiguration(string samplesDirectory, int refinedBoundingArea, int polygonBoundingArea)
{
    var processStartInfo = BuildChildProcessStartInfo(samplesDirectory);
    processStartInfo.Environment["DISCOVERY_MIN_REFINED_COMPONENT_BOUNDING_AREA"] = refinedBoundingArea.ToString(CultureInfo.InvariantCulture);
    processStartInfo.Environment["DISCOVERY_MIN_POLYGON_BOUNDING_AREA"] = polygonBoundingArea.ToString(CultureInfo.InvariantCulture);

    using var process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Could not start tuning child process.");
    var standardOutput = process.StandardOutput.ReadToEnd();
    var standardError = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"Child evaluation failed with exit code {process.ExitCode}.{Environment.NewLine}{standardError}");
    }

    var childResult = ConfigurationEvaluation.Parse(standardOutput);
    return childResult with
    {
        RefinedBoundingArea = refinedBoundingArea,
        PolygonBoundingArea = polygonBoundingArea
    };
}

static ProcessStartInfo BuildChildProcessStartInfo(string samplesDirectory)
{
    var currentAssemblyPath = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("Could not determine current assembly path.");
    var startInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"\"{currentAssemblyPath}\" --child-evaluate --samples \"{samplesDirectory}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    foreach (System.Collections.DictionaryEntry environmentVariable in Environment.GetEnvironmentVariables())
    {
        var variableName = environmentVariable.Key?.ToString();
        var variableValue = environmentVariable.Value?.ToString();
        if (string.IsNullOrWhiteSpace(variableName) ||
            string.IsNullOrWhiteSpace(variableValue) ||
            !variableName.StartsWith("DISCOVERY_", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        startInfo.Environment[variableName] = variableValue;
    }

    return startInfo;
}

static void WriteEvaluation(ConfigurationEvaluation evaluation)
{
    Console.WriteLine($"Samples:   {evaluation.SamplesDirectory}");
    Console.WriteLine($"Refined:   {evaluation.RefinedBoundingArea}");
    Console.WriteLine($"Polygon:   {evaluation.PolygonBoundingArea}");
    Console.WriteLine($"Evaluated: {evaluation.SampleResults.Count}");
    Console.WriteLine($"Total:     {evaluation.TotalScore:F6}");
    Console.WriteLine($"Average:   {evaluation.AverageScore:F6}");
    Console.WriteLine();
    Console.WriteLine("Per sample:");

    foreach (var sample in evaluation.SampleResults.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(
            $"{sample.FileName,-16} score={sample.Score:F6} iou={sample.IoU:F6} " +
            $"clusters={sample.ClusterCount} expectedContours={sample.ExpectedContours} actualContours={sample.ActualContours}");
    }
}

static void WriteHelp()
{
    Console.WriteLine("Discovery.Tuning");
    Console.WriteLine("Scores generated sample annotations against the expected gold-standard overlays.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project Discovery.Tuning -- [--samples <path>] [--refined <area>] [--polygon <area>]");
    Console.WriteLine("  dotnet run --project Discovery.Tuning -- --sweep [--samples <path>] --refined-values 10000,15000 --polygon-values 30000,35000");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --samples         Samples directory. Defaults to Discovery/bin/Debug/net9.0-windows/samples");
    Console.WriteLine("  --refined         Minimum refined-component bounding area for single evaluation");
    Console.WriteLine("  --polygon         Minimum final polygon bounding area for single evaluation");
    Console.WriteLine("  --sweep           Evaluates every refined/polygon combination from the provided lists");
    Console.WriteLine("  --refined-values  Comma-separated refined bounding areas for sweep mode");
    Console.WriteLine("  --polygon-values  Comma-separated polygon bounding areas for sweep mode");
    Console.WriteLine("  --help            Shows this help");
}

static int RunChildEvaluation(CommandLineOptions options)
{
    var evaluation = EvaluateCurrentProcessConfiguration(options.SamplesDirectory);
    Console.Write(ConfigurationEvaluation.Serialize(evaluation));
    return 0;
}

static ConfigurationEvaluation EvaluateCurrentProcessConfiguration(string samplesDirectory)
{
    var sampleFiles = Directory
        .EnumerateFiles(samplesDirectory, "*.sample.png", SearchOption.TopDirectoryOnly)
        .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var processor = new SampleImageProcessor();
    var sampleResults = new List<SampleScore>();

    foreach (var sampleFile in sampleFiles)
    {
        var expectedPath = Path.Combine(samplesDirectory, Path.GetFileNameWithoutExtension(sampleFile) + ".expected.png");
        if (!File.Exists(expectedPath))
        {
            continue;
        }

        var analysis = processor.AnalyzeImageFile(sampleFile);
        var score = ScoreSample(sampleFile, analysis.Result.OutputPath, expectedPath, analysis.PlayfieldDetection.Bounds, analysis.Result.ClusterCount);
        sampleResults.Add(score);
    }

    var refinedBoundingArea = ReadAreaFromEnvironment("DISCOVERY_MIN_REFINED_COMPONENT_BOUNDING_AREA", 15_000);
    var polygonBoundingArea = ReadAreaFromEnvironment("DISCOVERY_MIN_POLYGON_BOUNDING_AREA", 35_000);
    return new ConfigurationEvaluation(samplesDirectory, refinedBoundingArea, polygonBoundingArea, sampleResults);
}

static int ReadAreaFromEnvironment(string variableName, int fallbackValue)
{
    var rawValue = Environment.GetEnvironmentVariable(variableName);
    return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
        ? value
        : fallbackValue;
}

static SampleScore ScoreSample(string originalPath, string annotatedPath, string expectedPath, Rect playfieldBounds, int clusterCount)
{
    using var original = Cv2.ImRead(originalPath);
    using var annotated = Cv2.ImRead(annotatedPath);
    using var expected = Cv2.ImRead(expectedPath);

    var crop = ClampRect(playfieldBounds, original.Width, original.Height);
    using var originalRoi = new Mat(original, crop);
    using var annotatedRoi = new Mat(annotated, crop);
    using var expectedRoi = new Mat(expected, crop);
    using var actualMask = BuildOverlayMask(originalRoi, annotatedRoi);
    using var expectedMask = BuildOverlayMask(originalRoi, expectedRoi);
    using var intersectionMask = new Mat();
    using var unionMask = new Mat();

    Cv2.BitwiseAnd(actualMask, expectedMask, intersectionMask);
    Cv2.BitwiseOr(actualMask, expectedMask, unionMask);

    var actualContours = CountContours(actualMask);
    var expectedContours = CountContours(expectedMask);
    var intersection = Cv2.CountNonZero(intersectionMask);
    var union = Cv2.CountNonZero(unionMask);
    var iou = union == 0 ? 1.0 : intersection / (double)union;
    var contourPenalty = Math.Abs(actualContours - expectedContours) * 0.08;
    var score = Math.Max(0.0, iou - contourPenalty);

    return new SampleScore(
        Path.GetFileName(originalPath),
        score,
        iou,
        clusterCount,
        expectedContours,
        actualContours);
}

static Rect ClampRect(Rect rect, int maxWidth, int maxHeight)
{
    var x = Math.Clamp(rect.X, 0, Math.Max(0, maxWidth - 1));
    var y = Math.Clamp(rect.Y, 0, Math.Max(0, maxHeight - 1));
    var width = Math.Clamp(rect.Width, 1, maxWidth - x);
    var height = Math.Clamp(rect.Height, 1, maxHeight - y);
    return new Rect(x, y, width, height);
}

static Mat BuildOverlayMask(Mat original, Mat overlay)
{
    using var difference = new Mat();
    Cv2.Absdiff(original, overlay, difference);

    var channels = difference.Split();
    try
    {
        using var maxBlueGreen = new Mat();
        using var maxAllChannels = new Mat();
        Cv2.Max(channels[0], channels[1], maxBlueGreen);
        Cv2.Max(maxBlueGreen, channels[2], maxAllChannels);

        var threshold = new Mat();
        Cv2.Threshold(maxAllChannels, threshold, 25, 255, ThresholdTypes.Binary);

        SuppressOverlayNoise(threshold);

        using var dilated = new Mat();
        using var closed = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7));
        Cv2.Dilate(threshold, dilated, kernel, iterations: 2);
        Cv2.MorphologyEx(dilated, closed, MorphTypes.Close, kernel, iterations: 2);

        var filled = new Mat(closed.Size(), MatType.CV_8UC1, Scalar.Black);
        Cv2.FindContours(closed, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            if (Cv2.ContourArea(contour) < 400)
            {
                continue;
            }

            Cv2.DrawContours(filled, [contour], -1, Scalar.White, -1);
        }

        threshold.Dispose();
        return filled;
    }
    finally
    {
        foreach (var channel in channels)
        {
            channel.Dispose();
        }
    }
}

static void SuppressOverlayNoise(Mat mask)
{
    var border = Math.Min(20, Math.Min(mask.Width / 8, mask.Height / 8));
    if (border > 0)
    {
        mask[new Rect(0, 0, mask.Width, border)].SetTo(Scalar.Black);
        mask[new Rect(0, mask.Height - border, mask.Width, border)].SetTo(Scalar.Black);
        mask[new Rect(0, 0, border, mask.Height)].SetTo(Scalar.Black);
        mask[new Rect(mask.Width - border, 0, border, mask.Height)].SetTo(Scalar.Black);
    }

    var topLabelHeight = Math.Min(60, mask.Height / 6);
    var bottomLabelHeight = Math.Min(24, mask.Height / 10);
    if (topLabelHeight > 0)
    {
        mask[new Rect(0, 0, mask.Width, topLabelHeight)].SetTo(Scalar.Black);
    }

    if (bottomLabelHeight > 0)
    {
        mask[new Rect(0, mask.Height - bottomLabelHeight, mask.Width, bottomLabelHeight)].SetTo(Scalar.Black);
    }
}

static int CountContours(Mat mask)
{
    Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
    return contours.Count(contour => Cv2.ContourArea(contour) >= 400);
}

internal sealed record CommandLineOptions(
    RunMode Mode,
    string SamplesDirectory,
    int RefinedBoundingArea,
    int PolygonBoundingArea,
    int[] RefinedSweepValues,
    int[] PolygonSweepValues,
    bool ShowHelp)
{
    public static CommandLineOptions Parse(string[] args)
    {
        var showHelp = args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
                       args.Contains("-h", StringComparer.OrdinalIgnoreCase);
        var mode = args.Contains("--child-evaluate", StringComparer.OrdinalIgnoreCase)
            ? RunMode.ChildEvaluate
            : args.Contains("--sweep", StringComparer.OrdinalIgnoreCase)
                ? RunMode.Sweep
                : RunMode.Evaluate;

        var samplesDirectory = GetOptionValue(args, "--samples") ?? GetDefaultSamplesDirectory();
        var refinedBoundingArea = GetInt32OptionValue(args, "--refined", 15_000);
        var polygonBoundingArea = GetInt32OptionValue(args, "--polygon", 35_000);
        var refinedSweepValues = GetInt32ListOptionValue(args, "--refined-values");
        var polygonSweepValues = GetInt32ListOptionValue(args, "--polygon-values");

        if (!Directory.Exists(samplesDirectory))
        {
            throw new DirectoryNotFoundException($"Samples directory was not found: {samplesDirectory}");
        }

        return new CommandLineOptions(
            mode,
            samplesDirectory,
            refinedBoundingArea,
            polygonBoundingArea,
            refinedSweepValues,
            polygonSweepValues,
            showHelp);
    }

    private static string GetDefaultSamplesDirectory()
    {
        var startupDirectory = AppContext.BaseDirectory;
        var discoveryOutputDirectory = Path.GetFullPath(Path.Combine(startupDirectory, "..", "..", "..", "..", "Discovery", "bin", "Debug", "net9.0-windows", "samples"));
        return discoveryOutputDirectory;
    }

    private static string? GetOptionValue(IReadOnlyList<string> args, string optionName)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int GetInt32OptionValue(IReadOnlyList<string> args, string optionName, int fallbackValue)
    {
        var value = GetOptionValue(args, optionName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) && parsedValue > 0
            ? parsedValue
            : fallbackValue;
    }

    private static int[] GetInt32ListOptionValue(IReadOnlyList<string> args, string optionName)
    {
        var value = GetOptionValue(args, optionName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.Parse(item, NumberStyles.Integer, CultureInfo.InvariantCulture))
            .Where(item => item > 0)
            .Distinct()
            .Order()
            .ToArray();
    }
}

internal enum RunMode
{
    Evaluate,
    Sweep,
    ChildEvaluate
}

internal sealed record SampleScore(
    string FileName,
    double Score,
    double IoU,
    int ClusterCount,
    int ExpectedContours,
    int ActualContours);

internal sealed record ConfigurationEvaluation(
    string SamplesDirectory,
    int RefinedBoundingArea,
    int PolygonBoundingArea,
    IReadOnlyList<SampleScore> SampleResults)
{
    public double TotalScore => SampleResults.Sum(item => item.Score);

    public double AverageScore => SampleResults.Count == 0 ? 0.0 : TotalScore / SampleResults.Count;

    public static string Serialize(ConfigurationEvaluation evaluation)
    {
        var lines = new List<string>
        {
            evaluation.SamplesDirectory,
            evaluation.RefinedBoundingArea.ToString(CultureInfo.InvariantCulture),
            evaluation.PolygonBoundingArea.ToString(CultureInfo.InvariantCulture)
        };

        lines.AddRange(evaluation.SampleResults.Select(
            item => string.Join(
                "|",
                item.FileName,
                item.Score.ToString("R", CultureInfo.InvariantCulture),
                item.IoU.ToString("R", CultureInfo.InvariantCulture),
                item.ClusterCount.ToString(CultureInfo.InvariantCulture),
                item.ExpectedContours.ToString(CultureInfo.InvariantCulture),
                item.ActualContours.ToString(CultureInfo.InvariantCulture))));

        return string.Join(Environment.NewLine, lines);
    }

    public static ConfigurationEvaluation Parse(string serialized)
    {
        var lines = serialized
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 3)
        {
            throw new InvalidOperationException("Serialized evaluation output was incomplete.");
        }

        var sampleResults = lines
            .Skip(3)
            .Select(ParseSampleScore)
            .ToArray();

        return new ConfigurationEvaluation(
            lines[0],
            int.Parse(lines[1], NumberStyles.Integer, CultureInfo.InvariantCulture),
            int.Parse(lines[2], NumberStyles.Integer, CultureInfo.InvariantCulture),
            sampleResults);
    }

    private static SampleScore ParseSampleScore(string line)
    {
        var parts = line.Split('|');
        if (parts.Length != 6)
        {
            throw new InvalidOperationException($"Could not parse sample score line: {line}");
        }

        return new SampleScore(
            parts[0],
            double.Parse(parts[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
            double.Parse(parts[2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
            int.Parse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture),
            int.Parse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture),
            int.Parse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture));
    }
}
