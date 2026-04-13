using System.Windows;

namespace Discovery;

public partial class MainWindow
{
    private readonly SampleImageProcessor m_SampleImageProcessor = new();
    private readonly ScreenCaptureService m_ScreenCaptureService = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBox.Text = "Processing sample screenshots...";

        try
        {
            var summary = await Task.Run(() => m_SampleImageProcessor.ProcessSamples(ProjectRootLocator.ResolveFromBaseDirectory(AppContext.BaseDirectory)));
            StatusTextBox.Text = SampleProcessingSummaryFormatter.BuildSummaryText(summary);
        }
        catch (Exception ex)
        {
            StatusTextBox.Text = $"Processing failed:{Environment.NewLine}{ex}";
        }
    }

    private async void CaptureScreen_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBox.Text = "Capturing current screen and processing it...";

        try
        {
            var summary = await Task.Run(() => m_ScreenCaptureService.CaptureAndProcessCurrentScreen(ProjectRootLocator.ResolveFromBaseDirectory(AppContext.BaseDirectory)));
            StatusTextBox.Text = ScreenCaptureSummaryFormatter.BuildSummaryText(summary);
        }
        catch (Exception ex)
        {
            StatusTextBox.Text = $"Capture failed:{Environment.NewLine}{ex}";
        }
    }
}
