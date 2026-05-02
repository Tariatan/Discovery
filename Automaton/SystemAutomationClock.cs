namespace Automaton;

internal sealed class SystemAutomationClock : IAutomationClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
