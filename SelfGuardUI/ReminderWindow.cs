using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SelfGuardUI;

/// <summary>Full-screen, top-most reminder with a 10 → 0 countdown (code-only WPF window).</summary>
public sealed class ReminderWindow : Window
{
    public event Action? Finished;
    private int _remaining = 10;
    private readonly TextBlock _counter;
    private readonly DispatcherTimer _timer;
    private readonly bool _isPrimary;

    public ReminderWindow(bool isPrimary)
    {
        _isPrimary = isPrimary;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F));
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var message = new TextBlock
        {
            Text = "اتق الله و لا تعصه في خلوتك",
            FontFamily = new FontFamily("Segoe UI, Traditional Arabic, Arial"),
            FontSize = 72,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(40, 0, 40, 40)
        };

        _counter = new TextBlock
        {
            Text = _remaining.ToString(),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 200,
            FontWeight = FontWeights.Black,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
            HorizontalAlignment = HorizontalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight
        };

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(message);
        panel.Children.Add(_counter);
        Content = panel;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        Loaded += (_, _) => { Activate(); _timer.Start(); };

        // Block Alt+F4 / close while counting (user can still use Ctrl+Alt+Del — by design, not trapped).
        Closing += (_, e) => { if (_remaining > 0) e.Cancel = true; };
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        _counter.Text = Math.Max(_remaining, 0).ToString();
        Topmost = true;
        if (_remaining <= 0)
        {
            _timer.Stop();
            if (_isPrimary) Finished?.Invoke();
        }
    }
}
