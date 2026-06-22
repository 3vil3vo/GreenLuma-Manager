using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GreenLuma_Manager.Controllers;

public class NotificationManager
{
    private readonly UIElement _toast;
    private readonly System.Windows.Controls.TextBlock _toastMessage;
    private readonly System.Windows.Shapes.Path _toastIcon;
    private readonly System.Windows.Shapes.Shape _statusIndicator;
    private readonly System.Windows.Controls.TextBlock _txtStatus;
    private readonly System.Windows.Controls.TextBlock _txtGameCount;
    private readonly System.Windows.Controls.TextBlock? _txtLoadingDots;
    private readonly DispatcherTimer? _loadingDotsTimer;
    private int _loadingDotCount;

    public NotificationManager(
        UIElement toast,
        System.Windows.Controls.TextBlock toastMessage,
        System.Windows.Shapes.Path toastIcon,
        System.Windows.Shapes.Shape statusIndicator,
        System.Windows.Controls.TextBlock txtStatus,
        System.Windows.Controls.TextBlock txtGameCount,
        System.Windows.Controls.TextBlock? txtLoadingDots = null)
    {
        _toast = toast;
        _toastMessage = toastMessage;
        _toastIcon = toastIcon;
        _statusIndicator = statusIndicator;
        _txtStatus = txtStatus;
        _txtGameCount = txtGameCount;
        _txtLoadingDots = txtLoadingDots!;

        if (_txtLoadingDots != null)
        {
            _loadingDotsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _loadingDotsTimer.Tick += LoadingDotsTimer_Tick;
        }
    }

    public void StartLoadingDots()
    {
        _loadingDotCount = 0;
        _loadingDotsTimer?.Start();
    }

    public void StopLoadingDots()
    {
        _loadingDotsTimer?.Stop();
        if (_txtLoadingDots != null)
            _txtLoadingDots.Text = string.Empty;
    }

    private void LoadingDotsTimer_Tick(object? sender, EventArgs e)
    {
        _loadingDotCount = (_loadingDotCount + 1) % 4;
        if (_txtLoadingDots != null)
            _txtLoadingDots.Text = new string('.', _loadingDotCount);
    }

    public void ShowToast(string message, bool isSuccess = true)
    {
        _toastMessage.Text = message;

        if (_toastIcon != null)
        {
            var successBrush = System.Windows.Application.Current.TryFindResource("Success") as Brush ?? Brushes.Green;
            var dangerBrush = System.Windows.Application.Current.TryFindResource("Danger") as Brush ?? Brushes.Red;
            _toastIcon.Fill = isSuccess ? successBrush : dangerBrush;
        }

        _toast.Visibility = Visibility.Visible;
        _toast.Opacity = 0.0;

        var storyboard = new Storyboard();

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200));
        Storyboard.SetTarget(fadeIn, _toast);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fadeIn);

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(6000)
        };
        Storyboard.SetTarget(fadeOut, _toast);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fadeOut);

        storyboard.Completed += (_, _) =>
        {
            _toast.Visibility = Visibility.Collapsed;
            _toast.Opacity = 1.0;
        };

        storyboard.Begin();
    }

    public void SetStatusIndicator(Brush color, string text)
    {
        var storyboard = new Storyboard();

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150));
        Storyboard.SetTarget(fadeOut, _txtStatus);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));

        fadeOut.Completed += (_, _) =>
        {
            _statusIndicator.Fill = color;
            _txtStatus.Text = text;

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150));
            Storyboard.SetTarget(fadeIn, _txtStatus);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));

            var storyboardIn = new Storyboard();
            storyboardIn.Children.Add(fadeIn);
            storyboardIn.Begin();
        };

        storyboard.Children.Add(fadeOut);
        storyboard.Begin();
    }

    public void UpdateGameCount(int count, bool hasFilter = false)
    {
        if (count == 0)
        {
            _txtGameCount.Text = "No games";
        }
        else
        {
            var gameWord = count == 1 ? "game" : "games";
            _txtGameCount.Text = hasFilter
                ? $"{count} {gameWord} (filtered)"
                : $"{count} {gameWord}";
        }
    }

    public void UpdateLoadingText(string text)
    {
        if (_txtLoadingDots != null)
            _txtLoadingDots.Text = text;
    }
}
