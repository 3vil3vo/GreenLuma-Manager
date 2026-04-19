using System.Globalization;
using System.IO;
using GreenLuma_Manager.Services;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace GreenLuma_Manager.Utilities;

public class IconUrlConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string iconUrl || string.IsNullOrWhiteSpace(iconUrl))
            return null;
        try
        {
            if (iconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(iconUrl, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnDemand;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 64;
                bmp.EndInit();
                bmp.DownloadFailed += (_, args) =>
                    LogService.LogError("IconUrlConverter.DownloadFailed", args.ErrorException);
                return bmp;
            }

            if (File.Exists(iconUrl))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(iconUrl, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }

            return null;
        }
        catch (Exception ex)
        {
            LogService.LogError("IconUrlConverter.Convert", ex);
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}