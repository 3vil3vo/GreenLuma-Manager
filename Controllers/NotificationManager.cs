using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GreenLuma_Manager.Controllers;

public class NotificationManager
{
    private const int ToastDurationMs = 6000;
    private readonly DispatcherTimer _loadingDotsTimer;
    private readonly Shape _statusIndicator;

    private readonly UIElement _toast;
    private readonly Path _toastIcon;
    private readonly TextBlock _toastMessage;
    private readonly TextBlock _txtGameCount;
    private readonly TextBlock? _txtLoadingDots;
    private readonly TextBlock _txtStatus;
    private int _dotsCount;

    public NotificationManager(
        UIElement toast,
        TextBlock toastMessage,
        Path toastIcon,
        Shape statusIndicator,
        TextBlock txtStatus,
        TextBlock txtGameCount,
        TextBlock? txtLoadingDots)
    {
        _toast = toast;
        _toastMessage = toastMessage;
        _toastIcon = toastIcon;
        _statusIndicator = statusIndicator;
        _txtStatus = txtStatus;
        _txtGameCount = txtGameCount;
        _txtLoadingDots = txtLoadingDots;

        _loadingDotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _loadingDotsTimer.Tick += (_, _) =>
        {
            _dotsCount = (_dotsCount + 1) % 4;
            if (_txtLoadingDots != null)
                _txtLoadingDots.Text = new string('.', _dotsCount == 0 ? 1 : _dotsCount);
        };
    }

    public void StartLoadingDots()
    {
        _dotsCount = 1;
        if (_txtLoadingDots != null)
            _txtLoadingDots.Text = ".";
        _loadingDotsTimer.Start();
    }

    public void StopLoadingDots()
    {
        _loadingDotsTimer.Stop();
    }

    public void UpdateLoadingText(string text)
    {
        if (_txtLoadingDots != null)
            _txtLoadingDots.Text = text;
    }

    public void ShowToast(string message, bool isSuccess = true)
    {
        _toastMessage.Text = message;
        _toastIcon.Fill = isSuccess
            ? GetResource("Success") ?? Brushes.Green
            : GetResource("Danger") ?? Brushes.Red;

        _toast.Visibility = Visibility.Visible;
        _toast.Opacity = 0.0;

        var storyboard = new Storyboard();

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200));
        Storyboard.SetTarget(fadeIn, _toast);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fadeIn);

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(ToastDurationMs)
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

    public void UpdateGameCount(int count, bool isFiltered = false)
    {
        _txtGameCount.Text = count switch
        {
            0 => "No games",
            1 when !isFiltered => "1 game",
            1 when isFiltered => "1 game (filtered)",
            _ when !isFiltered => $"{count} games",
            _ => $"{count} games (filtered)"
        };
    }

    private Brush? GetResource(string key)
    {
        if (Application.Current.MainWindow?.Resources[key] is Brush b) return b;
        return null;
    }
}