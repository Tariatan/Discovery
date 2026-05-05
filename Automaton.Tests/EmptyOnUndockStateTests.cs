using Automaton.MiningStates;
using OpenCvSharp;

namespace Automaton.Tests;

public sealed class EmptyOnUndockStateTests
{
    private const ushort VirtualKeyS = 0x53;

    [Fact]
    public void Execute_OverviewHasAsteroidBelts_ClicksBeltTabRandomBeltAndPressesS()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var overviewPath = Path.Combine(workspace.Path, "overview.png");
        SyntheticMiningImageFactory.WriteWarpToAsteroidFieldImage(overviewPath);
        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                File.Copy(overviewPath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        var automationInputController = new StubAutomationInputController();
        var state = new EmptyOnUndockState(new AsteroidBeltOverviewDetector(), _ => 1);
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
        Assert.Equal(MiningAutomationStateKind.WarpingToAsteroidField, transition.NextState);
        Assert.Equal(MiningAutomationActionKind.WarpToAsteroidField, transition.Action);
        Assert.NotNull(transition.AsteroidBeltOverview);
        Assert.Equal(2, captureInvocationCount);
        Assert.Equal(2, automationInputController.ClickCount);
        Assert.Equal(new[] { 300, 300 }, automationInputController.Delays);
        Assert.Equal(2, automationInputController.MoveTargets.Count);
        Assert.Equal(new[] { VirtualKeyS }, automationInputController.KeyInputs);
        Assert.InRange(automationInputController.MoveTargets[0].X, 2270, 2315);
        Assert.InRange(automationInputController.MoveTargets[0].Y, 330, 365);
        Assert.InRange(automationInputController.MoveTargets[1].X, 1990, 2525);
        Assert.InRange(automationInputController.MoveTargets[1].Y, 490, 530);
    }

    [Fact]
    public void Execute_OverviewMissing_TransitionsToRecoveryWithoutClicking()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var undockedPath = Path.Combine(workspace.Path, "undocked.png");
        SyntheticMiningImageFactory.WriteUndockedCompleteImage(undockedPath);
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath => File.Copy(undockedPath, outputPath, overwrite: true)),
            new SampleImageProcessor());
        var automationInputController = new StubAutomationInputController();
        var state = new EmptyOnUndockState(new AsteroidBeltOverviewDetector(), _ => 0);
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
        Assert.Equal(MiningAutomationStateKind.Recovery, transition.NextState);
        Assert.Equal(MiningAutomationActionKind.Recover, transition.Action);
        Assert.Equal(0, automationInputController.ClickCount);
        Assert.Empty(automationInputController.MoveTargets);
        Assert.Empty(automationInputController.Delays);
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

        public List<ushort> KeyInputs { get; } = [];

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
            cancellationToken.ThrowIfCancellationRequested();
            KeyInputs.Add(virtualKey);
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
        public DateTime UtcNow { get; } = new(2026, 5, 3, 12, 0, 0, DateTimeKind.Utc);
    }
}
