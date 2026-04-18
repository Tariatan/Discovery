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
        Closed += MainWindow_Closed;
    }

    private async void Automate_Click(object sender, RoutedEventArgs e)
    {
        if (m_IsAutomationRunning)
        {
            StopAutomation();
            return;
        }

        m_IsAutomationRunning = true;
        SetStartButtonState(isRunning: true);
        m_AutomationCancellationSource = new CancellationTokenSource();
        var dpi = VisualTreeHelper.GetDpi(this);

        try
        {
            m_AutomationTask = Task.Run(
                () => m_AutomationService.AutomateCurrentScreen(dpi, m_AutomationCancellationSource.Token),
                m_AutomationCancellationSource.Token);
            await m_AutomationTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            m_AutomationCancellationSource?.Dispose();
            m_AutomationCancellationSource = null;
            m_AutomationTask = null;
            m_IsAutomationRunning = false;
            SetStartButtonState(isRunning: false);
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

    private void Samples_Click(object sender, RoutedEventArgs e)
    {
        m_AutomationService.ProcessSamples();
    }
}