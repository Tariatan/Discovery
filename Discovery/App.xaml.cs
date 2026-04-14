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
        var summary = processor.ProcessSamples();

        Console.WriteLine($"Samples folder: {summary.SamplesDirectory}");
        Console.WriteLine($"Debug output:  {summary.OutputDirectory}");

        foreach (var result in summary.Results)
        {
            Console.WriteLine($"{result.FileName,-12} playfield={(result.PlayfieldFound ? "yes" : "no"),-3}  clusters={result.ClusterCount}  output={result.OutputPath}");
        }
    }
}
