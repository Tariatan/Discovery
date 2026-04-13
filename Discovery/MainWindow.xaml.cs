using System.IO;
using System.Windows;

namespace Discovery;

public partial class MainWindow : Window
{
    private readonly SampleImageProcessor m_SampleImageProcessor = new();

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
}
