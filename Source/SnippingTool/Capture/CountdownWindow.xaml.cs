using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SnippingTool.Core;

namespace SnippingTool.Capture;

public partial class CountdownWindow : Window
{
    private readonly int _seconds;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _finished;

    public Task<bool> Completion => _completion.Task;

    public CountdownWindow(int seconds)
    {
        InitializeComponent();
        _seconds = Math.Max(1, seconds);
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - ActualWidth) / 2;
            Top = work.Top + 38;
            Activate();
            Focus();
            Keyboard.Focus(this);
            _timer.Start();
            UpdateText();
        };
        CancelButton.Click += (_, _) => Finish(false);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
        };
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        UpdateText();
        if (_watch.Elapsed.TotalSeconds >= _seconds) Finish(true);
    }

    private void UpdateText()
    {
        double remaining = Math.Max(0, _seconds - _watch.Elapsed.TotalSeconds);
        int shown = Math.Max(1, (int)Math.Ceiling(remaining));
        CountText.Text = shown == 1 ? "1 second" : $"{shown} seconds";
    }

    private void Finish(bool completed)
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        _completion.TrySetResult(completed);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _timer.Stop();
        if (!_finished)
        {
            _finished = true;
            _completion.TrySetResult(false);
        }
    }
}
