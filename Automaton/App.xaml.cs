using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace Automaton;

public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var logFilePath = ApplicationLogging.Configure();
        Log.ForContext<App>().Information(
            "Automaton started. LogFilePath={LogFilePath}, Arguments={Arguments}",
            logFilePath,
            e.Args);

        try
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
        catch (Exception exception)
        {
            Log.ForContext<App>().Fatal(exception, "Automaton startup failed.");
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Log.ForContext<App>().Information("Automaton exited. ExitCode={ExitCode}", e.ApplicationExitCode);
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private static void RunSampleProcessing()
    {
        Log.ForContext<App>().Information("Command-line sample processing started.");
        var processor = new SampleImageProcessor();
        var summary = processor.ProcessSamples();
        Log.ForContext<App>().Information(
            "Command-line sample processing finished. SamplesDirectory={SamplesDirectory}, ResultCount={ResultCount}",
            summary.SamplesDirectory,
            summary.Results.Count);

        Console.WriteLine($"Samples folder: {summary.SamplesDirectory}");

        foreach (var result in summary.Results)
        {
            Console.WriteLine($"{result.FileName,-12} playfield={(result.PlayfieldFound ? "yes" : "no"),-3}  clusters={result.ClusterCount}  output={result.OutputPath}");
        }
    }
}
