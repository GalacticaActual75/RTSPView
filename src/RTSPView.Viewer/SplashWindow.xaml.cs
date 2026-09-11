using System.Windows;

namespace RTSPView.Viewer;

public partial class SplashWindow : Window
{
    public SplashWindow() => InitializeComponent();

    public void SetStatus(string status) => StatusText.Text = status;

    public void ShowFailure(string message)
    {
        LoadingBar.IsIndeterminate = false;
        LoadingBar.Value = 0;
        LoadingBar.Foreground = System.Windows.Media.Brushes.IndianRed;
        StatusText.Text = message;
        Topmost = false;
    }
}
