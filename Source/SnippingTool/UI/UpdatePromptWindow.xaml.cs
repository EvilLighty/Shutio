using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SnippingTool.Core;

namespace SnippingTool.UI;

/// <summary>
/// The update dialog: "Update available → Update now / Later", with checking /
/// downloading / up-to-date / failed states. Shown non-modally so it never blocks the app.
/// </summary>
public partial class UpdatePromptWindow : Window
{
    private readonly string _currentVersion;

    public event EventHandler? AcceptClicked;
    public event EventHandler? LaterClicked;
    public event EventHandler? SettingsClicked;

    public UpdatePromptWindow(string currentVersion)
    {
        _currentVersion = currentVersion;
        InitializeComponent();
        UpdateButton.Click += (_, _) => AcceptClicked?.Invoke(this, EventArgs.Empty);
        LaterButton.Click += (_, _) => LaterClicked?.Invoke(this, EventArgs.Empty);
        SettingsButton.Click += (_, _) => SettingsClicked?.Invoke(this, EventArgs.Empty);
        Loaded += (_, _) => { ThemeManager.ApplyWindowChrome(this); };
        SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int corners = 2;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
            }
            catch { }
        };
    }

    private void SetButtonsEnabled(bool enabled)
    {
        UpdateButton.IsEnabled = enabled;
        LaterButton.IsEnabled = enabled;
        SettingsButton.IsEnabled = enabled;
    }

    public void SetChecking()
    {
        HeadingText.Text = "Update";
        StatusText.Text = "Checking GitHub for the latest version…";
        DetailText.Text = "";
        SettingsButton.Visibility = Visibility.Collapsed;
        UpdateButton.Visibility = Visibility.Visible;
        LaterButton.Content = "Later";
        SetButtonsEnabled(false);
    }

    public void SetReady(string latestVersion, string releaseName)
    {
        HeadingText.Text = "Update available";
        StatusText.Text = $"Shutio {latestVersion} is out — you're running {_currentVersion}.";
        DetailText.Text = "The update replaces only the app files. Your snips, recordings and settings in Data\\ are kept, and the app restarts itself.";
        SettingsButton.Visibility = Visibility.Collapsed;
        UpdateButton.Visibility = Visibility.Visible;
        LaterButton.Content = "Later";
        SetButtonsEnabled(true);
    }

    public void SetUpToDate(string latestVersion)
    {
        HeadingText.Text = "Up to date";
        StatusText.Text = $"You're running {_currentVersion} — that's the latest release.";
        DetailText.Text = "";
        SettingsButton.Visibility = Visibility.Collapsed;
        UpdateButton.Visibility = Visibility.Collapsed;
        LaterButton.Content = "Close";
        SetButtonsEnabled(true);
    }

    public void SetBusy(string status)
    {
        HeadingText.Text = "Updating";
        StatusText.Text = status;
        DetailText.Text = "This can take a minute (the updater downloads nothing else — it verifies with the built-in self-test).";
        SetButtonsEnabled(false);
    }

    public void SetFailed(string message)
    {
        HeadingText.Text = "Update check failed";
        StatusText.Text = message;
        DetailText.Text = "Check the connection — or add a GitHub token in Settings → Update (the repo is private, so a token is needed to see its releases).";
        SettingsButton.Visibility = Visibility.Visible;
        LaterButton.Content = "Close";
        SetButtonsEnabled(true);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            LaterClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }
}
