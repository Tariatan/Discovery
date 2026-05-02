using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Automaton.Properties;
using Serilog;

namespace Automaton;

public partial class MainWindow
{
    private const int HotKeyId = 1;
    private const int WindowMessageHotKey = 0x0312;
    private const int MinimumPilotIndex = 1;
    private const int MaximumPilotIndex = 3;
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierShift = 0x0004;
    private const uint VirtualKeyF11 = 0x7A;
    private static readonly Brush StartBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0xB4, 0x3A));
    private static readonly Brush StopBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x34));
    private static readonly ILogger Logger = Log.ForContext<MainWindow>();

    private readonly ProjectDiscoveryAutomationService m_ProjectDiscoveryAutomationService = new();
    private HwndSource? m_WindowSource;
    private CancellationTokenSource? m_AutomationCancellationSource;
    private bool m_IsAutomationRunning;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowPosition();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        Logger.Information("Main window initialized.");
    }

    private async void Automate_Click(object sender, RoutedEventArgs e)
    {
        if (!StartButton.IsEnabled)
        {
            return;
        }

        if (m_IsAutomationRunning)
        {
            Logger.Information("Stop requested from automation button.");
            StopAutomation();
            return;
        }

        var initialPilotIndex = GetPilotIndex();
        Logger.Information("Start requested from automation button. InitialPilotIndex={InitialPilotIndex}", initialPilotIndex);
        await StartAutomationAsync(initialPilotIndex, new CancellationTokenSource());
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await TryRunStartupAutomationAsync();
    }

    private async Task TryRunStartupAutomationAsync()
    {
        if (m_IsAutomationRunning)
        {
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        m_AutomationCancellationSource = cancellationSource;
        SetStartButtonEnabled(isEnabled: false);
        SetPilotIndexControlsEnabled(isEnabled: false);
        ApplyDebugImageRetention();
        var initialPilotIndex = GetPilotIndex();

        try
        {
            Logger.Information("Checking launcher startup automation. InitialPilotIndex={InitialPilotIndex}", initialPilotIndex);
            var startupSummary = await Task.Run(
                () => m_ProjectDiscoveryAutomationService.PrepareAutomationFromLauncherStartup(initialPilotIndex, cancellationSource.Token),
                cancellationSource.Token);
            Logger.Information(
                "Launcher startup automation prepared. ShouldStartAutomation={ShouldStartAutomation}, PlayButtonFound={PlayButtonFound}, PilotLocated={PilotLocated}, PlayCapturePath={PlayCapturePath}, PilotCapturePath={PilotCapturePath}",
                startupSummary.ShouldStartAutomation,
                startupSummary.PlayButtonFound,
                startupSummary.PilotLocated,
                startupSummary.PlayButtonCapturePath,
                startupSummary.PilotCapturePath);
            if (!startupSummary.ShouldStartAutomation)
            {
                return;
            }

            await StartAutomationAsync(initialPilotIndex, cancellationSource);
            cancellationSource = null;
        }
        catch (OperationCanceledException)
        {
            Logger.Information("Launcher startup automation was canceled.");
        }
        finally
        {
            if (cancellationSource is not null)
            {
                cancellationSource.Dispose();
                m_AutomationCancellationSource = null;
                SetStartButtonState(isRunning: false);
                SetStartButtonEnabled(isEnabled: true);
                SetPilotIndexControlsEnabled(isEnabled: true);
            }
        }
    }

    private async Task StartAutomationAsync(int initialPilotIndex, CancellationTokenSource cancellationSource)
    {
        ApplyDebugImageRetention();
        m_IsAutomationRunning = true;
        SetStartButtonState(isRunning: true);
        SetStartButtonEnabled(isEnabled: true);
        SetPilotIndexControlsEnabled(isEnabled: false);
        m_AutomationCancellationSource = cancellationSource;
        var dpi = VisualTreeHelper.GetDpi(this);
        Logger.Information(
            "Automation started. InitialPilotIndex={InitialPilotIndex}, DpiScaleX={DpiScaleX}, DpiScaleY={DpiScaleY}",
            initialPilotIndex,
            dpi.DpiScaleX,
            dpi.DpiScaleY);

        try
        {
            var automationTask = Task.Run(
                () => m_ProjectDiscoveryAutomationService.AutomateCurrentScreen(dpi, initialPilotIndex, cancellationSource.Token),
                cancellationSource.Token);
            var summary = await automationTask;
            Logger.Information(
                "Automation completed. CapturePath={CapturePath}, FocusedCapturePath={FocusedCapturePath}, ClickedPointCount={ClickedPointCount}, MaximumSubmissionsReached={MaximumSubmissionsReached}, CurrentPilotIndex={CurrentPilotIndex}, TargetPilotIndex={TargetPilotIndex}, PilotSwitchSucceeded={PilotSwitchSucceeded}",
                summary.CaptureSummary.CapturePath,
                summary.FocusedCapturePath,
                summary.ClickedPointCount,
                summary.MaximumSubmissionsReached,
                summary.CurrentPilotIndex,
                summary.TargetPilotIndex,
                summary.PilotSwitchSucceeded);
        }
        catch (OperationCanceledException)
        {
            Logger.Information("Automation was canceled.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Automation failed.");
            throw;
        }
        finally
        {
            cancellationSource.Dispose();
            if (ReferenceEquals(m_AutomationCancellationSource, cancellationSource))
            {
                m_AutomationCancellationSource = null;
            }

            m_IsAutomationRunning = false;
            SetStartButtonState(isRunning: false);
            SetStartButtonEnabled(isEnabled: true);
            SetPilotIndexControlsEnabled(isEnabled: true);
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var windowInteropHelper = new WindowInteropHelper(this);
        m_WindowSource = HwndSource.FromHwnd(windowInteropHelper.Handle);
        m_WindowSource?.AddHook(WindowMessageHook);

        var registered = RegisterHotKey(
            windowInteropHelper.Handle,
            HotKeyId,
            ModifierShift | ModifierAlt,
            VirtualKeyF11);
        if (!registered)
        {
            Logger.Error("Could not register global hotkey Shift+Alt+F11.");
            throw new InvalidOperationException("Could not register global hotkey Shift+Alt+F11.");
        }

        Logger.Information("Registered global hotkey Shift+Alt+F11.");
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        Logger.Information("Main window closing.");
        StopAutomation();
        Settings.Default.FormLocation = new Point(Left, Top);
        Settings.Default.Save();
        var windowInteropHelper = new WindowInteropHelper(this);
        UnregisterHotKey(windowInteropHelper.Handle, HotKeyId);
        m_WindowSource?.RemoveHook(WindowMessageHook);
        m_WindowSource = null;
    }

    private void RestoreWindowPosition()
    {
        var savedPosition = Settings.Default.FormLocation;
        if (!IsWindowPositionVisible(savedPosition))
        {
            return;
        }

        Left = savedPosition.X;
        Top = savedPosition.Y;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WindowMessageHotKey && wParam.ToInt32() == HotKeyId)
        {
            handled = true;
            Logger.Information("Global hotkey activated.");
            Automate_Click(this, new RoutedEventArgs());
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static bool IsWindowPositionVisible(Point position)
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;

        return position.X >= left &&
               position.Y >= top &&
               position.X < right &&
               position.Y < bottom;
    }

    private void StopAutomation()
    {
        if (m_AutomationCancellationSource is not null)
        {
            Logger.Information("Automation cancellation requested.");
        }

        m_AutomationCancellationSource?.Cancel();
        m_IsAutomationRunning = false;
        SetStartButtonState(isRunning: false);
    }

    private void SetStartButtonState(bool isRunning)
    {
        StartButton.Content = isRunning ? "Stop" : "Start";
        StartButton.Background = isRunning ? StopBrush : StartBrush;
    }

    private void SetStartButtonEnabled(bool isEnabled)
    {
        StartButton.IsEnabled = isEnabled;
    }

    private void SetPilotIndexControlsEnabled(bool isEnabled)
    {
        PilotIndexDecreaseButton.IsEnabled = isEnabled;
        PilotIndexIncreaseButton.IsEnabled = isEnabled;
        DebugCheckBox.IsEnabled = isEnabled;
    }

    private void ApplyDebugImageRetention()
    {
        var keepDebugImages = DebugCheckBox.IsChecked == true;
        m_ProjectDiscoveryAutomationService.KeepDebugImages = keepDebugImages;
        Logger.Information("Debug image retention set. KeepDebugImages={KeepDebugImages}", keepDebugImages);
    }

    private void PilotIndexDecrease_Click(object sender, RoutedEventArgs e)
    {
        SetPilotIndex(GetPilotIndex() - 1);
    }

    private void PilotIndexIncrease_Click(object sender, RoutedEventArgs e)
    {
        SetPilotIndex(GetPilotIndex() + 1);
    }

    private void PilotIndexTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SetPilotIndex(GetPilotIndex());
    }

    private int GetPilotIndex()
    {
        var pilotIndex = int.TryParse(PilotIndexTextBox.Text, out var parsedPilotIndex)
            ? parsedPilotIndex
            : MinimumPilotIndex;
        pilotIndex = Math.Clamp(pilotIndex, MinimumPilotIndex, MaximumPilotIndex);
        SetPilotIndex(pilotIndex);
        return pilotIndex;
    }

    private void SetPilotIndex(int pilotIndex)
    {
        PilotIndexTextBox.Text = pilotIndex.ToString();
    }

    private void Samples_Click(object sender, RoutedEventArgs e)
    {
        Logger.Information("Sample processing requested from main window.");
        m_ProjectDiscoveryAutomationService.ProcessSamples();
    }
}
