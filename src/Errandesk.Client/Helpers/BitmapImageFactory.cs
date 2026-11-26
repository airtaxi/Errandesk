using Microsoft.UI.Xaml.Media.Imaging;

namespace Errandesk.Client.Helpers;

/// <summary>
/// Creates bitmap images from encoded image files. Must run on the UI thread.
/// </summary>
public static class BitmapImageFactory
{
    /// <summary>
    /// Decodes <paramref name="data"/>, scaled down to <paramref name="decodePixelWidth"/> pixels wide when it is greater than zero.
    /// </summary>
    public static async Task<BitmapImage> CreateAsync(byte[] data, int decodePixelWidth = 0)
    {
        var bitmapImage = new BitmapImage();
        if (decodePixelWidth > 0) bitmapImage.DecodePixelWidth = decodePixelWidth;
        using var stream = new MemoryStream(data, writable: false);
        await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
        return bitmapImage;
    }
}
