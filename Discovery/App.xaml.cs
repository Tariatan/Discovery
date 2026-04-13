using System.IO;
using System.Windows;

namespace Discovery;

public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--process-samples", StringComparer.OrdinalIgnoreCase))
        {
            RunSampleProcessing();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        window.Show();
        base.OnStartup(e);
    }

    private static void RunSampleProcessing()
    {
        var processor = new SampleImageProcessor();
        var summary = processor.ProcessSamples(ResolveProjectRoot());

        Console.WriteLine($"Samples folder: {summary.SamplesDirectory}");
        Console.WriteLine($"Debug output:  {summary.OutputDirectory}");

        foreach (var result in summary.Results)
        {
            Console.WriteLine($"{result.FileName,-12} playfield={(result.PlayfieldFound ? "yes" : "no"),-3}  clusters={result.ClusterCount}  output={result.OutputPath}");
        }
    }

    private static string ResolveProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

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
