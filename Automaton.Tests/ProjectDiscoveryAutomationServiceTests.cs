using System.Drawing.Imaging;
using OpenCvSharp;

namespace Automaton.Tests;

public sealed class ProjectDiscoveryAutomationServiceTests
{
    private const ushort VirtualKeyAlt = 0x12;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyEnter = 0x0D;
    private const ushort VirtualKeyL = 0x4C;
    private const ushort VirtualKeyQ = 0x51;
    private const ushort VirtualKeyShift = 0x10;
    private const ushort VirtualKeyW = 0x57;

    [Fact]
    public void AutomateCurrentScreen_PlayfieldAndControlButtonExist_ClicksPolygonPointsAndFocusesControlButton()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(capturePath);
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath => File.Copy(capturePath, outputPath)),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        using var cancellationTokenSource = new CancellationTokenSource();
        var sawAfterSubmitDelay = false;
        var shortDelaysAfterSubmit = 0;
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds),
            OnDelay = milliseconds =>
            {
                if (milliseconds == 4_000)
                {
                    sawAfterSubmitDelay = true;
                    return;
                }

                if (sawAfterSubmitDelay && milliseconds == 300)
                {
                    shortDelaysAfterSubmit++;
                    if (shortDelaysAfterSubmit >= 2)
                    {
                        cancellationTokenSource.Cancel();
                    }
                }
            }
        };
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);
        AutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(summary.CaptureSummary.Analysis.Result.PlayfieldFound);
        Assert.True(summary.ClickedPointCount > 0);
        Assert.NotNull(summary.ControlButtonBounds);
        Assert.False(summary.MaximumSubmissionsReached);
        Assert.Equal("captures", summary.CaptureSummary.CapturesDirectory);
        Assert.Equal(summary.ClickedPointCount + 1, automationInputController.MoveTargets.Count); // + Control button focus
        Assert.Equal(summary.ClickedPointCount + 3, automationInputController.ClickCount);
        var finalMoveTarget = automationInputController.MoveTargets[^1];
        Assert.InRange(finalMoveTarget.X, 930, 1200);
        Assert.InRange(finalMoveTarget.Y, 645, 655);
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.CaptureSummary.CapturePath)));
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.CaptureSummary.Analysis.Result.OutputPath)));
        var focusedCaptureAbsolutePath = Path.Combine(workspace.Path, summary.FocusedCapturePath);
        Assert.True(File.Exists(focusedCaptureAbsolutePath));
        Assert.False(File.Exists(Path.ChangeExtension(focusedCaptureAbsolutePath, ".annotated.png")));
        Assert.Equal(
            Path.Combine(
                summary.CaptureSummary.CapturesDirectory,
                $"{Path.GetFileNameWithoutExtension(summary.CaptureSummary.CapturePath)}.focused.png"),
            summary.FocusedCapturePath);
    }

    [Fact]
    public void AutomateCurrentScreen_DebugImagesDisabled_DeletesCycleTraceImages()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(capturePath);
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath => File.Copy(capturePath, outputPath)),
            new SampleImageProcessor());
        using var cancellationTokenSource = new CancellationTokenSource();
        var sawAfterSubmitDelay = false;
        var shortDelaysAfterSubmit = 0;
        var automationInputController = new StubAutomationInputController
        {
            OnDelay = milliseconds =>
            {
                if (milliseconds == 4_000)
                {
                    sawAfterSubmitDelay = true;
                    return;
                }

                if (sawAfterSubmitDelay && milliseconds == 300)
                {
                    shortDelaysAfterSubmit++;
                    if (shortDelaysAfterSubmit >= 2)
                    {
                        cancellationTokenSource.Cancel();
                    }
                }
            }
        };
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController)
        {
            KeepDebugImages = false
        };
        var dpi = new System.Windows.DpiScale(1.0, 1.0);
        AutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.False(File.Exists(Path.Combine(workspace.Path, summary.CaptureSummary.CapturePath)));
        Assert.False(File.Exists(Path.Combine(workspace.Path, summary.CaptureSummary.Analysis.Result.OutputPath)));
        Assert.False(File.Exists(Path.Combine(workspace.Path, summary.FocusedCapturePath)));
        Assert.True(File.Exists(capturePath));
    }

    [Fact]
    public void AutomateCurrentScreen_StopRequestedAfterFirstCycle_StartsNextCycleOnlyWhenNotCanceled()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(capturePath);
        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                File.Copy(capturePath, outputPath);
            }),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        using var cancellationTokenSource = new CancellationTokenSource();
        var sawAfterSubmitDelay = false;
        var shortDelaysAfterSubmit = 0;
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds),
            OnDelay = milliseconds =>
            {
                if (milliseconds == 4_000)
                {
                    sawAfterSubmitDelay = true;
                    return;
                }

                if (sawAfterSubmitDelay && milliseconds == 300)
                {
                    shortDelaysAfterSubmit++;
                    if (shortDelaysAfterSubmit >= 2)
                    {
                        cancellationTokenSource.Cancel();
                    }
                }
            }
        };
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.Equal(2, captureInvocationCount);
    }

    [Fact]
    public void AutomateCurrentScreen_MaximumSubmissionsPopupAppearsAfterSubmit_SelectsNextPilotAndContinues()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        var popupPath = Path.Combine(workspace.Path, "maximum-submissions.png");
        var pilotSelectionScreenPath = Path.Combine(workspace.Path, "pilot-selection.png");
        var pilotDirectory = Path.Combine(workspace.Path, "pilot");
        var pilotAvatarLocation = new Point(240, 180);
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(capturePath);
        SyntheticDiscoveryImageFactory.WriteMaximumSubmissionsPopupImage(popupPath);
        WritePilotAvatarTemplates(pilotDirectory, 3);
        WritePilotSelectionScreen(pilotSelectionScreenPath, pilotAvatarLocation);

        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                var sourcePath = captureInvocationCount switch
                {
                    1 => capturePath,
                    2 => popupPath,
                    _ => pilotSelectionScreenPath
                };
                File.Copy(sourcePath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        using var cancellationTokenSource = new CancellationTokenSource();
        var pilotUnlockPressed = false;
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds),
            OnDelay = milliseconds =>
            {
                if (pilotUnlockPressed && milliseconds == 300)
                {
                    cancellationTokenSource.Cancel();
                }
            },
            OnPressKeyChord = (modifierVirtualKey, virtualKey) =>
            {
                if (modifierVirtualKey == VirtualKeyAlt && virtualKey == VirtualKeyL)
                {
                    pilotUnlockPressed = true;
                }
            }
        };
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);
        AutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.AutomateCurrentScreen(dpi, 2, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(summary.MaximumSubmissionsReached);
        Assert.True(summary.PilotSwitchSucceeded);
        Assert.Equal(3, summary.CurrentPilotIndex);
        Assert.Equal(3, summary.TargetPilotIndex);
        Assert.Equal(3, captureInvocationCount);
        Assert.Equal(summary.ClickedPointCount + 2, automationInputController.ClickCount);
        Assert.Equal(new Point(pilotAvatarLocation.X + 32, pilotAvatarLocation.Y + 32), automationInputController.MoveTargets[^1]);
        Assert.Equal(3, automationInputController.KeyboardInputs.Count);
        AssertKeyChord(automationInputController.KeyboardInputs[0], VirtualKeyAlt, VirtualKeyQ);
        AssertKey(automationInputController.KeyboardInputs[1], VirtualKeyEnter);
        AssertKeyChord(automationInputController.KeyboardInputs[2], VirtualKeyAlt, VirtualKeyL);
        Assert.Contains(20_000, automationInputController.Delays);
        Assert.Equal(300, automationInputController.Delays[^1]);
    }

    [Fact]
    public void AutomateCurrentScreen_MaximumSubmissionsPopupAppearsOnLastPilot_LogsOutAndStopsWithoutSwitchingPilot()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        var popupPath = Path.Combine(workspace.Path, "maximum-submissions.png");
        var pilotDirectory = Path.Combine(workspace.Path, "pilot");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(capturePath);
        SyntheticDiscoveryImageFactory.WriteMaximumSubmissionsPopupImage(popupPath);
        WritePilotAvatarTemplates(pilotDirectory, 1);
        WritePilotAvatarTemplates(pilotDirectory, 2);
        WritePilotAvatarTemplates(pilotDirectory, 3);

        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                var sourcePath = captureInvocationCount switch
                {
                    1 => capturePath,
                    _ => popupPath
                };
                File.Copy(sourcePath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        using var cancellationTokenSource = new CancellationTokenSource();
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds)
        };
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);
        AutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.AutomateCurrentScreen(dpi, 3, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(summary.MaximumSubmissionsReached);
        Assert.False(summary.PilotSwitchSucceeded);
        Assert.Equal(3, summary.CurrentPilotIndex);
        Assert.Equal(3, summary.TargetPilotIndex);
        Assert.Null(summary.PilotSwitchCapturePath);
        Assert.Equal(2, captureInvocationCount);
        Assert.Equal(summary.ClickedPointCount + 1, automationInputController.ClickCount);
        Assert.Equal(2, automationInputController.KeyboardInputs.Count);
        AssertKeyChord(automationInputController.KeyboardInputs[0], VirtualKeyAlt, VirtualKeyShift, VirtualKeyQ);
        AssertKey(automationInputController.KeyboardInputs[1], VirtualKeyEnter);
        Assert.Equal(2_000, automationInputController.Delays[^1]);
    }

    [Fact]
    public void AutomateCurrentScreen_FocusedCaptureContainsPlayfieldAndPopupEvidence_DoesNotSwitchPilot()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        var focusedCapturePath = Path.Combine(workspace.Path, "focused-capture.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(capturePath);
        SyntheticDiscoveryImageFactory.WriteMaximumSubmissionsPopupImageWithPlayfield(focusedCapturePath);
        Assert.True(new MaximumSubmissionsPopupDetector().Detect(focusedCapturePath));

        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                File.Copy(captureInvocationCount == 1 ? capturePath : focusedCapturePath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        using var cancellationTokenSource = new CancellationTokenSource();
        var sawAfterSubmitDelay = false;
        var shortDelaysAfterSubmit = 0;
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds),
            OnDelay = milliseconds =>
            {
                if (milliseconds == 4_000)
                {
                    sawAfterSubmitDelay = true;
                    return;
                }

                if (sawAfterSubmitDelay && milliseconds == 300)
                {
                    shortDelaysAfterSubmit++;
                    if (shortDelaysAfterSubmit >= 2)
                    {
                        cancellationTokenSource.Cancel();
                    }
                }
            }
        };
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);
        AutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.False(summary.MaximumSubmissionsReached);
        Assert.Equal(2, captureInvocationCount);
        Assert.Equal(summary.ClickedPointCount + 3, automationInputController.ClickCount);
        Assert.Empty(automationInputController.KeyboardInputs);
    }

    [Fact]
    public void AutomateCurrentScreen_MaximumSubmissionsPopupAppearsWithNoNextPilotConfigured_LogsOutWithoutPilotSelection()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        var popupPath = Path.Combine(workspace.Path, "maximum-submissions.png");
        SyntheticDiscoveryImageFactory.WriteTwoClusterImage(capturePath);
        SyntheticDiscoveryImageFactory.WriteMaximumSubmissionsPopupImage(popupPath);

        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                File.Copy(captureInvocationCount == 1 ? capturePath : popupPath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        using var cancellationTokenSource = new CancellationTokenSource();
        var automationInputController = new StubAutomationInputController();
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);
        AutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(summary.MaximumSubmissionsReached);
        Assert.False(summary.PilotSwitchSucceeded);
        Assert.Equal(1, summary.CurrentPilotIndex);
        Assert.Equal(1, summary.TargetPilotIndex);
        Assert.Equal(2, captureInvocationCount);
        Assert.Equal(summary.ClickedPointCount + 1, automationInputController.ClickCount);
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.FocusedCapturePath)));
        Assert.True(CountMaximumSubmissionsDebugOverlayPixels(Path.Combine(workspace.Path, summary.FocusedCapturePath)) > 0);
        Assert.Null(summary.PilotSwitchCapturePath);
        Assert.Equal(2, automationInputController.KeyboardInputs.Count);
        AssertKeyChord(automationInputController.KeyboardInputs[0], VirtualKeyAlt, VirtualKeyShift, VirtualKeyQ);
        AssertKey(automationInputController.KeyboardInputs[1], VirtualKeyEnter);
        Assert.Equal(2_000, automationInputController.Delays[^1]);
    }

    [Fact]
    public void ScalePointForDpi_PointUsesScaledDisplayCoordinates_ReturnsDevicePixelPoint()
    {
        // Arrange
        var point = new Point(1065, 650);
        var dpi = new System.Windows.DpiScale(1.25, 1.25);

        // Act
        var scaledPoint = ProjectDiscoveryAutomationService.ScalePointForDpi(point, dpi);

        // Assert
        Assert.Equal(1331, scaledPoint.X);
        Assert.Equal(813, scaledPoint.Y);
    }

    [Fact]
    public void PrepareAutomationFromLauncherStartup_PlayButtonIsMissing_DrawsDebugOverlayWithoutInput()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var startupCapturePath = Path.Combine(workspace.Path, "startup.png");
        WriteBlankStartupScreen(startupCapturePath);
        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                File.Copy(startupCapturePath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        var automationInputController = new StubAutomationInputController();
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController);
        StartupAutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.PrepareAutomationFromLauncherStartup(1, CancellationToken.None);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.False(summary.PlayButtonFound);
        Assert.False(summary.ShouldStartAutomation);
        Assert.Equal(1, captureInvocationCount);
        Assert.Empty(automationInputController.MoveTargets);
        Assert.Equal(0, automationInputController.ClickCount);
        Assert.Empty(automationInputController.KeyboardInputs);
        Assert.True(File.Exists(Path.Combine(workspace.Path, summary.PlayButtonCapturePath)));
        Assert.True(CountDebugOverlayPixels(Path.Combine(workspace.Path, summary.PlayButtonCapturePath)) > 0);
    }

    [Fact]
    public void PrepareAutomationFromLauncherStartup_DebugImagesDisabled_DeletesStartupTraceImages()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var startupCapturePath = Path.Combine(workspace.Path, "startup.png");
        WriteBlankStartupScreen(startupCapturePath);
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath => File.Copy(startupCapturePath, outputPath, overwrite: true)),
            new SampleImageProcessor());
        var automationInputController = new StubAutomationInputController();
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController)
        {
            KeepDebugImages = false
        };
        StartupAutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.PrepareAutomationFromLauncherStartup(1, CancellationToken.None);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.False(summary.PlayButtonFound);
        Assert.False(File.Exists(Path.Combine(workspace.Path, summary.PlayButtonCapturePath)));
        Assert.True(File.Exists(startupCapturePath));
    }

    [Fact]
    public void PrepareAutomationFromLauncherStartup_PlayButtonAndPilotExist_ClicksLauncherAndUnlocksPilot()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var playButtonLocation = new Point(320, 140);
        var pilotAvatarLocation = new Point(240, 180);
        var startupCapturePath = Path.Combine(workspace.Path, "startup.png");
        var pilotSelectionScreenPath = Path.Combine(workspace.Path, "pilot-selection.png");
        var pilotDirectory = Path.Combine(workspace.Path, "pilot");
        WritePlayButtonScreen(startupCapturePath, playButtonLocation);
        WritePilotAvatarTemplates(pilotDirectory, 1);
        WritePilotSelectionScreen(pilotSelectionScreenPath, pilotAvatarLocation);

        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                File.Copy(captureInvocationCount == 1 ? startupCapturePath : pilotSelectionScreenPath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        var automationInputController = new StubAutomationInputController();
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController);
        StartupAutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.PrepareAutomationFromLauncherStartup(1, CancellationToken.None);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(summary.PlayButtonFound);
        Assert.True(summary.PilotLocated);
        Assert.True(summary.ShouldStartAutomation);
        Assert.Equal(2, captureInvocationCount);
        Assert.Equal(2, automationInputController.ClickCount);
        Assert.Equal(new[] { 20_000, 20_000 }, automationInputController.Delays);
        Assert.Equal(2, automationInputController.KeyboardInputs.Count);
        AssertKeyChord(automationInputController.KeyboardInputs[0], VirtualKeyControl, VirtualKeyW);
        AssertKeyChord(automationInputController.KeyboardInputs[1], VirtualKeyAlt, VirtualKeyL);
        Assert.Equal(new Point(pilotAvatarLocation.X + 32, pilotAvatarLocation.Y + 32), automationInputController.MoveTargets[^1]);
        Assert.InRange(automationInputController.MoveTargets[0].X, playButtonLocation.X, playButtonLocation.X + 257);
        Assert.InRange(automationInputController.MoveTargets[0].Y, playButtonLocation.Y, playButtonLocation.Y + 69);
    }

    [Fact]
    public void PrepareAutomationFromLauncherStartup_PilotIsMissing_DrawsDebugOverlayWithoutUnlock()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var startupCapturePath = Path.Combine(workspace.Path, "startup.png");
        var pilotSelectionScreenPath = Path.Combine(workspace.Path, "pilot-selection.png");
        WritePlayButtonScreen(startupCapturePath, new Point(320, 140));
        WriteBlankStartupScreen(pilotSelectionScreenPath);

        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                File.Copy(captureInvocationCount == 1 ? startupCapturePath : pilotSelectionScreenPath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        var automationInputController = new StubAutomationInputController();
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController);
        StartupAutomationSummary summary;

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            summary = automationService.PrepareAutomationFromLauncherStartup(1, CancellationToken.None);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(summary.PlayButtonFound);
        Assert.False(summary.PilotLocated);
        Assert.False(summary.ShouldStartAutomation);
        Assert.Equal(2, captureInvocationCount);
        Assert.Equal(1, automationInputController.ClickCount);
        Assert.Equal(new[] { 20_000 }, automationInputController.Delays);
        Assert.Single(automationInputController.KeyboardInputs);
        AssertKeyChord(automationInputController.KeyboardInputs[0], VirtualKeyControl, VirtualKeyW);
        Assert.NotNull(summary.PilotCapturePath);
        Assert.True(CountPilotNotFoundDebugOverlayPixels(Path.Combine(workspace.Path, summary.PilotCapturePath)) > 0);
    }

    [Fact]
    public void AutomateCurrentScreen_CancellationRequested_StopsBeforeAnyClicks()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(_ => throw new InvalidOperationException("Capture should not run when automation is already canceled.")),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds)
        };
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            Assert.Throws<OperationCanceledException>(() => automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token));
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.Empty(automationInputController.MoveTargets);
        Assert.Equal(0, automationInputController.ClickCount);
    }

    [Fact]
    public void AutomateCurrentScreen_SixthSubmitWouldOccurInsideWindow_WaitsUntilWindowExpires()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        SyntheticDiscoveryImageFactory.WriteSingleClusterImage(capturePath);
        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                File.Copy(capturePath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        using var cancellationTokenSource = new CancellationTokenSource();
        var submitTimes = new List<DateTime>();
        var observedLongRateLimitDelay = false;
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds => automationClock.AdvanceBy(milliseconds),
            OnDelay = milliseconds =>
            {
                if (milliseconds > 4_000)
                {
                    observedLongRateLimitDelay = true;
                }

                if (milliseconds == 4_000)
                {
                    submitTimes.Add(automationClock.UtcNow.AddMilliseconds(-milliseconds));
                    if (submitTimes.Count >= 6)
                    {
                        cancellationTokenSource.Cancel();
                    }
                }
            }
        };
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(captureInvocationCount >= 11);
        Assert.True(observedLongRateLimitDelay);
        Assert.True(submitTimes.Count >= 6);
        Assert.True((submitTimes[5] - submitTimes[0]).TotalMilliseconds >= 90_000);
        AssertNoMoreThanFiveSubmissionsPerMinute(submitTimes);
    }

    [Fact]
    public void AutomateCurrentScreen_PilotSwitchHappensAfterFifthSubmit_PreservesRateLimitForNextPilot()
    {
        // Arrange
        using var workspace = new TemporaryDirectory();
        var capturePath = Path.Combine(workspace.Path, "fixture-capture.png");
        var popupPath = Path.Combine(workspace.Path, "maximum-submissions.png");
        var pilotSelectionScreenPath = Path.Combine(workspace.Path, "pilot-selection.png");
        var pilotDirectory = Path.Combine(workspace.Path, "pilot");
        var pilotAvatarLocation = new Point(240, 180);
        SyntheticDiscoveryImageFactory.WriteSingleClusterImage(capturePath);
        SyntheticDiscoveryImageFactory.WriteMaximumSubmissionsPopupImage(popupPath);
        WritePilotAvatarTemplates(pilotDirectory, 2);
        WritePilotSelectionScreen(pilotSelectionScreenPath, pilotAvatarLocation);

        var captureInvocationCount = 0;
        var screenCaptureService = new ScreenCaptureService(
            new StubScreenCaptureProvider(outputPath =>
            {
                captureInvocationCount++;
                var sourcePath = captureInvocationCount switch
                {
                    10 => popupPath,
                    11 => pilotSelectionScreenPath,
                    _ => capturePath
                };
                File.Copy(sourcePath, outputPath, overwrite: true);
            }),
            new SampleImageProcessor());
        var automationClock = new StubAutomationClock();
        using var cancellationTokenSource = new CancellationTokenSource();
        var submitTimes = new List<DateTime>();
        var pilotSwitchCompleted = false;
        var observedPostSwitchRateLimitDelay = false;
        var automationInputController = new StubAutomationInputController
        {
            OnDelayAdvanceClock = milliseconds =>
            {
                if (milliseconds == 300 || milliseconds is >= 301 and <= 800)
                {
                    return;
                }

                automationClock.AdvanceBy(milliseconds);
            },
            OnDelay = milliseconds =>
            {
                if (pilotSwitchCompleted && milliseconds > 4_000)
                {
                    observedPostSwitchRateLimitDelay = true;
                }

                if (milliseconds == 4_000)
                {
                    submitTimes.Add(automationClock.UtcNow.AddMilliseconds(-milliseconds));
                    if (submitTimes.Count >= 6)
                    {
                        cancellationTokenSource.Cancel();
                    }
                }
            },
            OnPressKeyChord = (modifierVirtualKey, virtualKey) =>
            {
                if (modifierVirtualKey == VirtualKeyAlt && virtualKey == VirtualKeyL)
                {
                    pilotSwitchCompleted = true;
                }
            }
        };
        var automationService = new ProjectDiscoveryAutomationService(screenCaptureService, automationInputController, automationClock);
        var dpi = new System.Windows.DpiScale(1.0, 1.0);

        // Act
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workspace.Path);

        try
        {
            automationService.AutomateCurrentScreen(dpi, cancellationTokenSource.Token);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Assert
        Assert.True(observedPostSwitchRateLimitDelay);
        Assert.True(submitTimes.Count >= 6);
        Assert.True((submitTimes[5] - submitTimes[0]).TotalMilliseconds >= 90_000);
        AssertNoMoreThanFiveSubmissionsPerMinute(submitTimes);
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

        public List<KeyboardInput> KeyboardInputs { get; } = [];

        public int ClickCount { get; private set; }

        public Action<int>? OnDelay { get; init; }

        public Action<int>? OnDelayAdvanceClock { get; init; }

        public Action<ushort, ushort>? OnPressKeyChord { get; init; }

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
            KeyboardInputs.Add(new KeyboardInput(null, null, virtualKey));
        }

        public void PressKeyChord(ushort modifierVirtualKey, ushort virtualKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KeyboardInputs.Add(new KeyboardInput(modifierVirtualKey, null, virtualKey));
            OnPressKeyChord?.Invoke(modifierVirtualKey, virtualKey);
        }

        public void PressKeyChord(
            ushort firstModifierVirtualKey,
            ushort secondModifierVirtualKey,
            ushort virtualKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KeyboardInputs.Add(new KeyboardInput(firstModifierVirtualKey, secondModifierVirtualKey, virtualKey));
        }

        public void Delay(int milliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(milliseconds);
            OnDelayAdvanceClock?.Invoke(milliseconds);
            OnDelay?.Invoke(milliseconds);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private readonly record struct KeyboardInput(
        ushort? ModifierVirtualKey,
        ushort? SecondModifierVirtualKey,
        ushort VirtualKey);

    private sealed class StubAutomationClock : IAutomationClock
    {
        private DateTime m_UtcNow = new(2026, 4, 22, 12, 0, 0, DateTimeKind.Utc);

        public DateTime UtcNow => m_UtcNow;

        public void AdvanceBy(int milliseconds)
        {
            m_UtcNow = m_UtcNow.AddMilliseconds(milliseconds);
        }
    }

    private static int CountMaximumSubmissionsDebugOverlayPixels(string imagePath)
    {
        using var image = Cv2.ImRead(imagePath);
        if (image.Empty())
        {
            return 0;
        }

        using var region = new Mat(image, new Rect(0, 0, Math.Min(700, image.Width), Math.Min(80, image.Height)));
        using var mask = new Mat();
        Cv2.InRange(region, new Scalar(70, 110, 240), new Scalar(90, 130, 255), mask);
        return Cv2.CountNonZero(mask);
    }

    private static int CountPilotNotFoundDebugOverlayPixels(string imagePath)
    {
        return CountDebugOverlayPixels(imagePath);
    }

    private static int CountDebugOverlayPixels(string imagePath)
    {
        using var image = Cv2.ImRead(imagePath);
        if (image.Empty())
        {
            return 0;
        }

        using var region = new Mat(image, new Rect(0, 0, Math.Min(700, image.Width), Math.Min(80, image.Height)));
        using var mask = new Mat();
        Cv2.InRange(region, new Scalar(70, 110, 240), new Scalar(90, 130, 255), mask);
        return Cv2.CountNonZero(mask);
    }

    private static void WriteBlankStartupScreen(string outputPath)
    {
        using var image = new Mat(new Size(900, 640), MatType.CV_8UC3, new Scalar(18, 18, 18));
        Cv2.ImWrite(outputPath, image);
    }

    private static void WritePlayButtonScreen(string outputPath, Point playButtonLocation)
    {
        using var screen = new Mat(new Size(900, 640), MatType.CV_8UC3, new Scalar(18, 18, 18));
        using var playButton = LoadPlayButtonImage();
        using var region = new Mat(screen, new Rect(playButtonLocation.X, playButtonLocation.Y, playButton.Width, playButton.Height));
        playButton.CopyTo(region);
        Cv2.ImWrite(outputPath, screen);
    }

    private static Mat LoadPlayButtonImage()
    {
        using var bitmap = Properties.Resources.play;
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        return Cv2.ImDecode(memoryStream.ToArray(), ImreadModes.Color);
    }

    private static void WritePilotAvatarTemplates(string pilotDirectory, int pilotIndex)
    {
        Directory.CreateDirectory(pilotDirectory);
        using var avatar = CreatePilotAvatarTemplate(focused: false);
        using var focusedAvatar = CreatePilotAvatarTemplate(focused: true);
        Cv2.ImWrite(Path.Combine(pilotDirectory, $"{pilotIndex}.png"), avatar);
        Cv2.ImWrite(Path.Combine(pilotDirectory, $"{pilotIndex}_focused.png"), focusedAvatar);
    }

    private static void WritePilotSelectionScreen(string outputPath, Point pilotAvatarLocation)
    {
        using var screen = new Mat(new Size(900, 640), MatType.CV_8UC3, new Scalar(18, 18, 18));
        using var focusedAvatar = CreatePilotAvatarTemplate(focused: true);
        using var region = new Mat(screen, new Rect(pilotAvatarLocation.X, pilotAvatarLocation.Y, focusedAvatar.Width, focusedAvatar.Height));
        focusedAvatar.CopyTo(region);
        Cv2.ImWrite(outputPath, screen);
    }

    private static Mat CreatePilotAvatarTemplate(bool focused)
    {
        var image = new Mat(new Size(64, 64), MatType.CV_8UC3, focused ? new Scalar(42, 70, 120) : new Scalar(85, 85, 85));
        Cv2.Rectangle(image, new Rect(6, 6, 52, 52), focused ? new Scalar(80, 130, 210) : new Scalar(120, 120, 120), -1);
        Cv2.Circle(image, new Point(32, 24), 12, focused ? new Scalar(130, 195, 245) : new Scalar(180, 180, 180), -1, LineTypes.AntiAlias);
        Cv2.Ellipse(image, new Point(32, 48), new Size(18, 10), 0, 0, 360, focused ? new Scalar(35, 95, 185) : new Scalar(65, 65, 65), -1, LineTypes.AntiAlias);
        Cv2.Line(image, new Point(10, 58), new Point(58, 10), focused ? new Scalar(210, 180, 60) : new Scalar(150, 150, 150), 2, LineTypes.AntiAlias);

        if (focused)
        {
            return image;
        }

        var grayImage = new Mat();
        Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
        image.Dispose();
        return grayImage;
    }

    private static void AssertKey(
        KeyboardInput keyInput,
        ushort virtualKey)
    {
        Assert.Null(keyInput.ModifierVirtualKey);
        Assert.Null(keyInput.SecondModifierVirtualKey);
        Assert.Equal(virtualKey, keyInput.VirtualKey);
    }

    private static void AssertKeyChord(
        KeyboardInput keyInput,
        ushort modifierVirtualKey,
        ushort virtualKey)
    {
        Assert.Equal(modifierVirtualKey, keyInput.ModifierVirtualKey);
        Assert.Null(keyInput.SecondModifierVirtualKey);
        Assert.Equal(virtualKey, keyInput.VirtualKey);
    }

    private static void AssertKeyChord(
        KeyboardInput keyInput,
        ushort firstModifierVirtualKey,
        ushort secondModifierVirtualKey,
        ushort virtualKey)
    {
        Assert.Equal(firstModifierVirtualKey, keyInput.ModifierVirtualKey);
        Assert.Equal(secondModifierVirtualKey, keyInput.SecondModifierVirtualKey);
        Assert.Equal(virtualKey, keyInput.VirtualKey);
    }

    private static void AssertNoMoreThanFiveSubmissionsPerMinute(IReadOnlyList<DateTime> submitTimes)
    {
        foreach (var windowStartedAt in submitTimes)
        {
            var windowEndedAt = windowStartedAt.AddMinutes(1);
            var at = windowStartedAt;
            var submissionsInWindow = submitTimes.Count(submitTime => submitTime >= at && submitTime < windowEndedAt);
            Assert.True(submissionsInWindow <= 5);
        }
    }
}
