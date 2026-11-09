using System.Windows;
using System.Windows.Threading;
using SnippingTool.Core;

namespace SnippingTool.Recording;

public partial class RecordingIndicatorWindow : Window
{
    private readonly RecordingController _controller;
    private readonly DispatcherTimer _timer;
    private bool _stopping;
    private bool _finished;

    public event EventHandler<RecordingStoppedEventArgs>? Finished;

    public RecordingIndicatorWindow(RecordingController controller)
    {
        InitializeComponent();
        _controller = controller;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            var elapsed = _controller.Elapsed;
            TimerText.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        };
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - 12;
            Top = work.Top + 10;
            _timer.Start();
        };
        StopButton.Click += async (_, _) => await StopAsync();
        Closing += (_, e) => { if (!_finished && !_stopping) e.Cancel = true; };
        Closed += (_, _) => _timer.Stop();
    }

    public async Task StopAsync()
    {
        if (_stopping || _finished) return;
        _stopping = true;
        StopButton.IsEnabled = false;
        TimerText.Text = "Saving…";
        try
        {
            var result = await _controller.StopAsync();
            _finished = true;
            _timer.Stop();
            Finished?.Invoke(this, result);
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error("Stopping recording failed", ex);
            _finished = true;
            _timer.Stop();
            Finished?.Invoke(this, new RecordingStoppedEventArgs
            {
                OutputPath = _controller.OutputPath,
                Success = false,
                Error = ex.Message
            });
            Close();
        }
    }
}
