namespace Automaton.MiningStates;

internal sealed record MiningAutomationStateTransition(
    MiningAutomationStateKind State,
    MiningAutomationStateKind NextState,
    MiningAutomationActionKind Action,
    string? CapturePath = null,
    DockedScreenAnalysis? DockedScreen = null,
    LocationChangeTimerLocation? LocationChangeTimer = null,
    AsteroidBeltOverviewAnalysis? AsteroidBeltOverview = null);
