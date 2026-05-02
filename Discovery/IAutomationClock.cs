namespace Discovery;

internal interface IAutomationClock
{
    DateTime UtcNow { get; }
}
