using System.Drawing;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SnippingTool.Imaging;

namespace SnippingTool.UI;

public partial class ToastWindow : Window
{
    private static ToastWindow? _current;
    private readonly DispatcherTimer _timer;
    private Action? _action;

    public ToastWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(6) };
        _timer.Tick += (_, _) => Close();
        CloseButton.Click += (_, _) => Close();
        ActionButton.Click += (_, _) =>
        {
            var action = _action;
            Close();
            action?.Invoke();
        };
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => { _timer.Stop(); _timer.Start(); };
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - 4;
            Top = work.Bottom - ActualHeight - 4;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            _timer.Start();
        };
    }

    public static void Show(string message, string? actionLabel = null, Action? action = null, Bitmap? thumbnail = null)
    {
        try
        {
            _current?.Close();
            var toast = new ToastWindow();
            toast.MessageText.Text = message;
            toast._action = action;
            if (!string.IsNullOrWhiteSpace(actionLabel) && action != null)
            {
                toast.ActionButton.Content = actionLabel;
                toast.ActionButton.Visibility = Visibility.Visible;
            }
            if (thumbnail != null)
            {
                toast.ThumbImage.Source = BitmapUtils.ToBitmapSource(thumbnail);
                toast.ThumbBorder.Visibility = Visibility.Visible;
            }
            _current = toast;
            toast.Closed += (_, _) => { if (ReferenceEquals(_current, toast)) _current = null; };
            toast.Show();
        }
        catch { }
    }
}
