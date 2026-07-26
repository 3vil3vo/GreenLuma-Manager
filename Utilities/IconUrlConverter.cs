using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using GreenLuma_Manager.Services;

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
                bmp.DecodeFailed += static (_, _) => { };
                bmp.CacheOption = BitmapCacheOption.OnDemand;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 64;
                bmp.EndInit();
                bmp.DownloadFailed += (_, args) =>
                    Logger.Error(args.ErrorException, "IconUrlConverter.DownloadFailed");
                return bmp;
            }

            if (File.Exists(iconUrl))
            {
                if (string.Equals(Path.GetExtension(iconUrl), ".ico", StringComparison.OrdinalIgnoreCase))
                    return LoadIco(iconUrl);

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
            Logger.Error(ex, "IconUrlConverter.Convert");
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static BitmapSource? LoadIco(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder =
                new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            var result = BitmapFrame.Create(frame);
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
    }
}