namespace Automaton.MiningStates;

internal interface IMiningAutomationState
{
    MiningAutomationStateKind Kind { get; }

    MiningAutomationStateTransition Execute(
        MiningAutomationContext context,
        CancellationToken cancellationToken);
}
