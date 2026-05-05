using OpenCvSharp;

namespace Automaton.MiningStates;

internal sealed class DockedState : IMiningAutomationState
{
    private const string CaptureSuffix = ".mining-docked";

    private readonly DockedScreenDetector m_Detector;

    public DockedState()
        : this(new DockedScreenDetector())
    {
    }

    internal DockedState(DockedScreenDetector detector)
    {
        m_Detector = detector;
    }

    public MiningAutomationStateKind Kind => MiningAutomationStateKind.Docked;

    public MiningAutomationStateTransition Execute(
        MiningAutomationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturePath = context.ScreenCaptureService.CaptureCurrentScreenTrace(CaptureSuffix);
        cancellationToken.ThrowIfCancellationRequested();

        using var screen = Cv2.ImRead(capturePath);
        var analysis = m_Detector.Analyze(screen);
        if (!analysis.IsDocked)
        {
            return new MiningAutomationStateTransition(
                Kind,
                MiningAutomationStateKind.Recovery,
                MiningAutomationActionKind.Recover,
                capturePath,
                analysis);
        }

        if (!analysis.MiningHoldFocused)
        {
            if (analysis.MiningHoldEntryBounds is null)
            {
                return new MiningAutomationStateTransition(
                    Kind,
                    MiningAutomationStateKind.Recovery,
                    MiningAutomationActionKind.Recover,
                    capturePath,
                    analysis);
            }

            context.ClickUiElement(Center(analysis.MiningHoldEntryBounds.Value), cancellationToken);
            return new MiningAutomationStateTransition(
                Kind,
                MiningAutomationStateKind.Docked,
                MiningAutomationActionKind.FocusMiningHold,
                capturePath,
                analysis);
        }

        if (analysis.MiningHoldContent == MiningHoldContentState.ContainsOre)
        {
            return new MiningAutomationStateTransition(
                Kind,
                MiningAutomationStateKind.UnloadCargo,
                MiningAutomationActionKind.UnloadCargo,
                capturePath,
                analysis);
        }

        if (analysis.MiningHoldContent != MiningHoldContentState.Empty ||
            analysis.UndockButtonBounds is null)
        {
            return new MiningAutomationStateTransition(
                Kind,
                MiningAutomationStateKind.Recovery,
                MiningAutomationActionKind.Recover,
                capturePath,
                analysis);
        }

        context.ClickUiElement(Center(analysis.UndockButtonBounds.Value), cancellationToken);
        return new MiningAutomationStateTransition(
            Kind,
            MiningAutomationStateKind.Undocking,
            MiningAutomationActionKind.Undock,
            capturePath,
            analysis);
    }

    private static Point Center(Rect bounds)
    {
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }
}
