using OpenCvSharp;

namespace Automaton.MiningStates;

internal sealed class EmptyOnUndockState : IMiningAutomationState
{
    private const string CaptureSuffix = ".mining-empty-on-undock";
    private const ushort VirtualKeyS = 0x53;

    private readonly AsteroidBeltOverviewDetector m_Detector;
    private readonly Func<int, int> m_NextRandomIndex;

    public EmptyOnUndockState()
        : this(new AsteroidBeltOverviewDetector(), Random.Shared.Next)
    {
    }

    internal EmptyOnUndockState(
        AsteroidBeltOverviewDetector detector,
        Func<int, int> nextRandomIndex)
    {
        m_Detector = detector;
        m_NextRandomIndex = nextRandomIndex;
    }

    public MiningAutomationStateKind Kind => MiningAutomationStateKind.EmptyOnUndock;

    public MiningAutomationStateTransition Execute(
        MiningAutomationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturePath = context.ScreenCaptureService.CaptureCurrentScreenTrace(CaptureSuffix);
        var analysis = Analyze(capturePath);
        if (!analysis.OverviewLocated ||
            analysis.OverviewBeltButtonBounds is null)
        {
            return Recover(capturePath, analysis);
        }

        context.ClickUiElement(Center(analysis.OverviewBeltButtonBounds.Value), cancellationToken);

        capturePath = context.ScreenCaptureService.CaptureCurrentScreenTrace(CaptureSuffix);
        analysis = Analyze(capturePath);
        if (analysis.AsteroidBelts.Count == 0)
        {
            return Recover(capturePath, analysis);
        }

        var selectedAsteroidBeltIndex = Math.Clamp(
            m_NextRandomIndex(analysis.AsteroidBelts.Count),
            0,
            analysis.AsteroidBelts.Count - 1);
        var selectedAsteroidBelt = analysis.AsteroidBelts[selectedAsteroidBeltIndex];
        context.ClickUiElement(Center(selectedAsteroidBelt.Bounds), cancellationToken);
        context.AutomationInputController.PressKey(VirtualKeyS, cancellationToken);
        return new MiningAutomationStateTransition(
            Kind,
            MiningAutomationStateKind.WarpingToAsteroidField,
            MiningAutomationActionKind.WarpToAsteroidField,
            capturePath,
            AsteroidBeltOverview: analysis);
    }

    private AsteroidBeltOverviewAnalysis Analyze(string capturePath)
    {
        using var screen = Cv2.ImRead(capturePath);
        return m_Detector.Analyze(screen);
    }

    private MiningAutomationStateTransition Recover(
        string capturePath,
        AsteroidBeltOverviewAnalysis analysis)
    {
        return new MiningAutomationStateTransition(
            Kind,
            MiningAutomationStateKind.Recovery,
            MiningAutomationActionKind.Recover,
            capturePath,
            AsteroidBeltOverview: analysis);
    }

    private static Point Center(Rect bounds)
    {
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }
}
