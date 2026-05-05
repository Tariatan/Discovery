using OpenCvSharp;

namespace Automaton.MiningStates;

internal sealed record MiningAutomationContext(
    ScreenCaptureService ScreenCaptureService,
    IAutomationInputController AutomationInputController,
    IAutomationClock AutomationClock)
{
    private const int UiClickDelayMilliseconds = 300;

    public void ClickUiElement(Point point, CancellationToken cancellationToken)
    {
        AutomationInputController.MoveTo(point);
        AutomationInputController.Delay(UiClickDelayMilliseconds, cancellationToken);
        AutomationInputController.LeftClick(cancellationToken);
    }
}
