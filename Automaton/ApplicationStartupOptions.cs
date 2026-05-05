namespace Automaton;

internal sealed record ApplicationStartupOptions(
    bool ProcessSamples,
    ApplicationAutomationMode AutomationMode)
{
    public static ApplicationStartupOptions Parse(IEnumerable<string> arguments)
    {
        var processSamples = arguments.Contains("--process-samples", StringComparer.OrdinalIgnoreCase);
        var automationMode = arguments.Any(IsMinerArgument)
            ? ApplicationAutomationMode.Mining
            : ApplicationAutomationMode.ProjectDiscovery;

        return new ApplicationStartupOptions(processSamples, automationMode);
    }

    private static bool IsMinerArgument(string argument)
    {
        return string.Equals(argument, "-miner", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(argument, "--miner", StringComparison.OrdinalIgnoreCase);
    }
}

public enum ApplicationAutomationMode
{
    ProjectDiscovery,
    Mining
}
