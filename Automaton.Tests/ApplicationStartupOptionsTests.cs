namespace Automaton.Tests;

public sealed class ApplicationStartupOptionsTests
{
    [Fact]
    public void Parse_NoAutomationModeArgument_UsesProjectDiscoveryMode()
    {
        // Arrange
        var arguments = Array.Empty<string>();

        // Act
        var options = ApplicationStartupOptions.Parse(arguments);

        // Assert
        Assert.False(options.ProcessSamples);
        Assert.Equal(ApplicationAutomationMode.ProjectDiscovery, options.AutomationMode);
    }

    [Fact]
    public void Parse_MinerArgument_UsesMiningMode()
    {
        // Arrange
        var arguments = new[] { "-miner" };

        // Act
        var options = ApplicationStartupOptions.Parse(arguments);

        // Assert
        Assert.False(options.ProcessSamples);
        Assert.Equal(ApplicationAutomationMode.Mining, options.AutomationMode);
    }

    [Fact]
    public void Parse_ProcessSamplesArgument_EnablesSampleProcessing()
    {
        // Arrange
        var arguments = new[] { "--process-samples" };

        // Act
        var options = ApplicationStartupOptions.Parse(arguments);

        // Assert
        Assert.True(options.ProcessSamples);
        Assert.Equal(ApplicationAutomationMode.ProjectDiscovery, options.AutomationMode);
    }
}
