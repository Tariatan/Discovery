using OpenCvSharp;
using System.Runtime.InteropServices;

namespace Discovery;

internal sealed class AutomationInputController : IAutomationInputController
{
    private const int MouseDownDurationMilliseconds = 250;
    private const uint LeftDownEvent = 0x0002;
    private const uint LeftUpEvent = 0x0004;
    private const uint KeyUpEvent = 0x0002;

    public void MoveTo(Point point)
    {
        SetCursorPos(point.X, point.Y);
    }

    public void LeftClick(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Thread.Sleep(MouseDownDurationMilliseconds);
        cancellationToken.ThrowIfCancellationRequested();
        var leftButtonPressed = false;

        try
        {
            mouse_event(LeftDownEvent, 0, 0, 0, UIntPtr.Zero);
            leftButtonPressed = true;
            Thread.Sleep(MouseDownDurationMilliseconds);
        }
        finally
        {
            if (leftButtonPressed)
            {
                mouse_event(LeftUpEvent, 0, 0, 0, UIntPtr.Zero);
            }
        }

        Thread.Sleep(MouseDownDurationMilliseconds);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void PressKey(ushort virtualKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        keybd_event((byte)virtualKey, 0, 0, UIntPtr.Zero);
        Thread.Sleep(MouseDownDurationMilliseconds);
        keybd_event((byte)virtualKey, 0, KeyUpEvent, UIntPtr.Zero);
        Thread.Sleep(MouseDownDurationMilliseconds);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void PressKeyChord(ushort modifierVirtualKey, ushort virtualKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        keybd_event((byte)modifierVirtualKey, 0, 0, UIntPtr.Zero);

        try
        {
            keybd_event((byte)virtualKey, 0, 0, UIntPtr.Zero);
            Thread.Sleep(MouseDownDurationMilliseconds);
            keybd_event((byte)virtualKey, 0, KeyUpEvent, UIntPtr.Zero);
        }
        finally
        {
            keybd_event((byte)modifierVirtualKey, 0, KeyUpEvent, UIntPtr.Zero);
        }

        Thread.Sleep(MouseDownDurationMilliseconds);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void PressKeyChord(
        ushort firstModifierVirtualKey,
        ushort secondModifierVirtualKey,
        ushort virtualKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        keybd_event((byte)firstModifierVirtualKey, 0, 0, UIntPtr.Zero);
        keybd_event((byte)secondModifierVirtualKey, 0, 0, UIntPtr.Zero);

        try
        {
            keybd_event((byte)virtualKey, 0, 0, UIntPtr.Zero);
            Thread.Sleep(MouseDownDurationMilliseconds);
            keybd_event((byte)virtualKey, 0, KeyUpEvent, UIntPtr.Zero);
        }
        finally
        {
            keybd_event((byte)secondModifierVirtualKey, 0, KeyUpEvent, UIntPtr.Zero);
            keybd_event((byte)firstModifierVirtualKey, 0, KeyUpEvent, UIntPtr.Zero);
        }

        Thread.Sleep(MouseDownDurationMilliseconds);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Delay(int milliseconds, CancellationToken cancellationToken)
    {
        cancellationToken.WaitHandle.WaitOne(milliseconds);
        cancellationToken.ThrowIfCancellationRequested();
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
