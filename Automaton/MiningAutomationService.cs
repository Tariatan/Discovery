using Automaton.MiningStates;
using Serilog;

namespace Automaton;

internal sealed class MiningAutomationService
{
    private const int StartupDelayMilliseconds = 3_000;
    private const int StepDelayMilliseconds = 500;
    private static readonly ILogger Logger = Log.ForContext<MiningAutomationService>();

    private readonly MiningAutomationContext m_Context;
    private IMiningAutomationState m_CurrentState;

    public MiningAutomationService()
        : this(new ScreenCaptureService(), new AutomationInputController(), new SystemAutomationClock())
    {
    }

    internal MiningAutomationService(
        ScreenCaptureService screenCaptureService,
        IAutomationInputController automationInputController,
        IAutomationClock automationClock)
    {
        m_Context = new MiningAutomationContext(screenCaptureService, automationInputController, automationClock);
        m_CurrentState = new DockedState();
    }

    public MiningAutomationStepSummary AutomateCurrentScreen(CancellationToken cancellationToken)
    {
        Logger.Information("Mining automation loop starting.");
        m_Context.AutomationInputController.Delay(StartupDelayMilliseconds, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        MiningAutomationStepSummary? lastSummary = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                lastSummary = ExecuteSingleStep(cancellationToken);
                m_Context.AutomationInputController.Delay(StepDelayMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (lastSummary is not null)
        {
            Logger.Information(
                "Mining automation loop canceled after a completed step. State={State}, NextState={NextState}, Action={Action}, CapturePath={CapturePath}",
                lastSummary.State,
                lastSummary.NextState,
                lastSummary.Action,
                lastSummary.CapturePath);
            return lastSummary;
        }

        return lastSummary ?? throw new OperationCanceledException(cancellationToken);
    }

    public MiningAutomationStepSummary ExecuteSingleStep(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transition = m_CurrentState.Execute(m_Context, cancellationToken);
        Logger.Information(
            "Mining automation step executed. State={State}, NextState={NextState}, Action={Action}, CapturePath={CapturePath}",
            transition.State,
            transition.NextState,
            transition.Action,
            transition.CapturePath);
        m_CurrentState = CreateState(transition.NextState);

        return new MiningAutomationStepSummary(
            transition.State,
            transition.NextState,
            transition.Action,
            transition.CapturePath,
            transition.DockedScreen,
            transition.LocationChangeTimer,
            transition.AsteroidBeltOverview);
    }

    private static IMiningAutomationState CreateState(MiningAutomationStateKind stateKind)
    {
        return stateKind switch
        {
            MiningAutomationStateKind.Docked => new DockedState(),
            MiningAutomationStateKind.Undocking => new UndockingState(),
            MiningAutomationStateKind.EmptyOnUndock => new EmptyOnUndockState(),
            _ => new PendingMiningAutomationState(stateKind)
        };
    }
}

internal sealed record MiningAutomationStepSummary(
    MiningAutomationStateKind State,
    MiningAutomationStateKind NextState,
    MiningAutomationActionKind Action,
    string? CapturePath,
    DockedScreenAnalysis? DockedScreen,
    LocationChangeTimerLocation? LocationChangeTimer = null,
    AsteroidBeltOverviewAnalysis? AsteroidBeltOverview = null);

internal enum MiningAutomationStateKind
{
    Docked,
    Undocking,
    EmptyOnUndock,
    WarpingToAsteroidField,
    UnloadCargo,
    Recovery
}

internal enum MiningAutomationActionKind
{
    None,
    FocusMiningHold,
    Undock,
    CompleteUndock,
    WarpToAsteroidField,
    UnloadCargo,
    Recover
}
