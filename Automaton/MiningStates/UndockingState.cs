using OpenCvSharp;

namespace Automaton.MiningStates;

internal sealed class UndockingState : IMiningAutomationState
{
    private const int InitialUndockDelayMilliseconds = 15_000;
    private const int LocationChangeTimerPollingMilliseconds = 1_000;
    private const int LocationChangeTimerPollingAttemptCount = 15;
    private const string CaptureSuffix = ".mining-undocking";

    private readonly LocationChangeTimerLocator m_Locator;

    public UndockingState()
        : this(new LocationChangeTimerLocator())
    {
    }

    internal UndockingState(LocationChangeTimerLocator locator)
    {
        m_Locator = locator;
    }

    public MiningAutomationStateKind Kind => MiningAutomationStateKind.Undocking;

    public MiningAutomationStateTransition Execute(
        MiningAutomationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.AutomationInputController.Delay(InitialUndockDelayMilliseconds, cancellationToken);

        string? capturePath = null;
        for (var attempt = 0; attempt < LocationChangeTimerPollingAttemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            capturePath = context.ScreenCaptureService.CaptureCurrentScreenTrace(CaptureSuffix);
            using var screen = Cv2.ImRead(capturePath);
            if (m_Locator.TryLocate(screen, out var location))
            {
                return new MiningAutomationStateTransition(
                    Kind,
                    MiningAutomationStateKind.EmptyOnUndock,
                    MiningAutomationActionKind.CompleteUndock,
                    capturePath,
                    LocationChangeTimer: location);
            }

            context.AutomationInputController.Delay(LocationChangeTimerPollingMilliseconds, cancellationToken);
        }

        return new MiningAutomationStateTransition(
            Kind,
            MiningAutomationStateKind.Recovery,
            MiningAutomationActionKind.Recover,
            capturePath);
    }
}
