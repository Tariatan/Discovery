using OpenCvSharp;

namespace Automaton;

internal interface IAutomationInputController
{
    void MoveTo(Point point);

    void LeftClick(CancellationToken cancellationToken);

    void PressKey(ushort virtualKey, CancellationToken cancellationToken);

    void PressKeyChord(ushort modifierVirtualKey, ushort virtualKey, CancellationToken cancellationToken);

    void PressKeyChord(
        ushort firstModifierVirtualKey,
        ushort secondModifierVirtualKey,
        ushort virtualKey,
        CancellationToken cancellationToken);

    void Delay(int milliseconds, CancellationToken cancellationToken);
}
