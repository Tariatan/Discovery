using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Discovery.Properties;

namespace Discovery;

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

    private readonly AutomationService m_AutomationService = new();
    private HwndSource? m_WindowSource;
    private CancellationTokenSource? m_AutomationCancellationSource;
    private Task? m_AutomationTask;
    private bool m_IsAutomationRunning;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowPosition();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void Automate_Click(object sender, RoutedEventArgs e)
    {
        if (!StartButton.IsEnabled)
        {
            return;
        }

        if (m_IsAutomationRunning)
        {
            StopAutomation();
            return;
        }

        await StartAutomationAsync(GetPilotIndex(), new CancellationTokenSource());
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
        var initialPilotIndex = GetPilotIndex();

        try
        {
            var startupSummary = await Task.Run(
                () => m_AutomationService.PrepareAutomationFromLauncherStartup(initialPilotIndex, cancellationSource.Token),
                cancellationSource.Token);
            if (!startupSummary.ShouldStartAutomation)
            {
                return;
            }

            await StartAutomationAsync(initialPilotIndex, cancellationSource);
            cancellationSource = null;
        }
        catch (OperationCanceledException)
        {
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
        m_IsAutomationRunning = true;
        SetStartButtonState(isRunning: true);
        SetStartButtonEnabled(isEnabled: true);
        SetPilotIndexControlsEnabled(isEnabled: false);
        m_AutomationCancellationSource = cancellationSource;
        var dpi = VisualTreeHelper.GetDpi(this);

        try
        {
            m_AutomationTask = Task.Run(
                () => m_AutomationService.AutomateCurrentScreen(dpi, initialPilotIndex, cancellationSource.Token),
                cancellationSource.Token);
            await m_AutomationTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellationSource.Dispose();
            if (ReferenceEquals(m_AutomationCancellationSource, cancellationSource))
            {
                m_AutomationCancellationSource = null;
            }

            m_AutomationTask = null;
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
            throw new InvalidOperationException("Could not register global hotkey Shift+Alt+F11.");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
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
        m_AutomationService.ProcessSamples();
    }
}
