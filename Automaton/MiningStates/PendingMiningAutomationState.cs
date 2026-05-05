namespace Automaton.MiningStates;

internal sealed class PendingMiningAutomationState(MiningAutomationStateKind kind) : IMiningAutomationState
{
    public MiningAutomationStateKind Kind { get; } = kind;

    public MiningAutomationStateTransition Execute(
        MiningAutomationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new MiningAutomationStateTransition(
            Kind,
            Kind,
            MiningAutomationActionKind.None);
    }
}
