using System.Globalization;
using System.IO;
using Serilog;
using Serilog.Events;

namespace Discovery;

internal static class ApplicationLogging
{
    private const string LogsFolderName = "logs";
    private const string LogFileTimestampFormat = "yyyy-MM-dd-HH-mm-ss";
    private const string LogFileExtension = ".log";
    private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    public static string Configure()
    {
        Directory.CreateDirectory(LogsFolderName);

        var logFilePath = Path.Combine(
            LogsFolderName,
            $"{DateTime.Now.ToString(LogFileTimestampFormat, CultureInfo.InvariantCulture)}{LogFileExtension}");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .WriteTo.File(
                logFilePath,
                outputTemplate: OutputTemplate)
            .CreateLogger();

        Log.Information("Logging started. Log file: {LogFilePath}", logFilePath);
        Log.Warning("This is a warning");
        Log.Error("This is an error");
        return logFilePath;
    }
}
