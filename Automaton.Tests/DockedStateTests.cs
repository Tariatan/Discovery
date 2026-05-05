using Automaton.MiningStates;
using OpenCvSharp;

namespace Automaton.Tests;

public sealed class DockedStateTests
{
    [Fact]
    public void Execute_ItemHangarFocused_ClicksMiningHoldAndStaysDocked()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "docked-item-hangar.png");
        SyntheticMiningImageFactory.WriteDockedItemHangarFocusedImage(capturePath);
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath => File.Copy(capturePath, outputPath)),
            new SampleImageProcessor());
        var automationInputController = new StubAutomationInputController();
        var state = new DockedState();
        MiningAutomationStateTransition transition;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            transition = state.Execute(
                new MiningAutomationContext(screenCaptureService, automationInputController, new StubAutomationClock()),
                CancellationToken.None);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.Equal(MiningAutomationStateKind.Docked, transition.NextState);
        Assert.Equal(MiningAutomationActionKind.FocusMiningHold, transition.Action);
        Assert.Single(automationInputController.MoveTargets);
        Assert.Equal(1, automationInputController.ClickCount);
        Assert.Equal(new[] { 300 }, automationInputController.Delays);
        Assert.NotNull(transition.DockedScreen?.MiningHoldEntryBounds);
        AssertPointInside(automationInputController.MoveTargets[0], transition.DockedScreen.MiningHoldEntryBounds!.Value);
    }

    [Fact]
    public void Execute_MiningHoldFocusedEmpty_ClicksUndockAndTransitionsToUndocking()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "docked-empty.png");
        SyntheticMiningImageFactory.WriteDockedMiningHoldFocusedEmptyImage(capturePath);
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath => File.Copy(capturePath, outputPath)),
            new SampleImageProcessor());
        var automationInputController = new StubAutomationInputController();
        var state = new DockedState();
        MiningAutomationStateTransition transition;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            transition = state.Execute(
                new MiningAutomationContext(screenCaptureService, automationInputController, new StubAutomationClock()),
                CancellationToken.None);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.Equal(MiningAutomationStateKind.Undocking, transition.NextState);
        Assert.Equal(MiningAutomationActionKind.Undock, transition.Action);
        Assert.Single(automationInputController.MoveTargets);
        Assert.Equal(1, automationInputController.ClickCount);
        Assert.Equal(new[] { 300 }, automationInputController.Delays);
        Assert.NotNull(transition.DockedScreen?.UndockButtonBounds);
        AssertPointInside(automationInputController.MoveTargets[0], transition.DockedScreen.UndockButtonBounds!.Value);
    }

    [Fact]
    public void Execute_MiningHoldFocusedNotEmpty_TransitionsToUnloadCargoWithoutClicking()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "docked-not-empty.png");
        SyntheticMiningImageFactory.WriteDockedMiningHoldFocusedNotEmptyImage(capturePath);
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath => File.Copy(capturePath, outputPath)),
            new SampleImageProcessor());
        var automationInputController = new StubAutomationInputController();
        var state = new DockedState();
        MiningAutomationStateTransition transition;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            transition = state.Execute(
                new MiningAutomationContext(screenCaptureService, automationInputController, new StubAutomationClock()),
                CancellationToken.None);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.Equal(MiningAutomationStateKind.UnloadCargo, transition.NextState);
        Assert.Equal(MiningAutomationActionKind.UnloadCargo, transition.Action);
        Assert.Empty(automationInputController.MoveTargets);
        Assert.Equal(0, automationInputController.ClickCount);
        Assert.Empty(automationInputController.Delays);
    }

    private static void AssertPointInside(Point point, Rect bounds)
    {
        Assert.InRange(point.X, bounds.Left, bounds.Right - 1);
        Assert.InRange(point.Y, bounds.Top, bounds.Bottom - 1);
    }

    private sealed class StubScreenCaptureProvider(Action<string> captureAction)
        : ScreenCaptureService.IScreenCaptureProvider
    {
        public void CaptureToFile(string outputPath)
        {
            captureAction(outputPath);
        }
    }

    private sealed class StubAutomationInputController : IAutomationInputController
    {
        public List<Point> MoveTargets { get; } = [];

        public List<int> Delays { get; } = [];

        public int ClickCount { get; private set; }

        public void MoveTo(Point point)
        {
            MoveTargets.Add(point);
        }

        public void LeftClick(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClickCount++;
        }

        public void PressKey(ushort virtualKey, CancellationToken cancellationToken)
        {
        }

        public void PressKeyChord(ushort modifierVirtualKey, ushort virtualKey, CancellationToken cancellationToken)
        {
        }

        public void PressKeyChord(
            ushort firstModifierVirtualKey,
            ushort secondModifierKey,
            ushort virtualKey,
            CancellationToken cancellationToken)
        {
        }

        public void Delay(int milliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(milliseconds);
        }
    }

    private sealed class StubAutomationClock : IAutomationClock
    {
        public DateTime UtcNow { get; } = new(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);
    }
}
