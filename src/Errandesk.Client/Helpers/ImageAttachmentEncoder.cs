using Errandesk.Client.Models;
using Errandesk.Contracts;
#if WINDOWS
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
#else
using SkiaSharp;
#endif

namespace Errandesk.Client.Helpers;

/// <summary>
/// Turns an image file into a JPEG for an assistant message: rotated upright by its EXIF orientation, transparent areas filled with white,
/// scaled down to at most <see cref="MaximumDimension"/> pixels on the longer side, and compressed until it fits <see cref="ConversationAttachmentLimits.MaximumLength"/>.
/// </summary>
public static class ImageAttachmentEncoder
{
    public const int MaximumDimension = 2048;

    private const string JpegMediaType = "image/jpeg";

    private static readonly (int MaximumDimension, int Quality)[] s_attempts = [(MaximumDimension, 85), (MaximumDimension, 70), (1536, 70), (1024, 60)];

    /// <summary>
    /// Decodes and re-encodes the image read from <paramref name="source"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The content is not an image this device can decode, or it stays too large.</exception>
    public static async Task<EncodedImage> EncodeAsync(Stream source, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        var data = buffer.ToArray();
        try
        {
            foreach (var (maximumDimension, quality) in s_attempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var encoded = await EncodeJpegAsync(data, maximumDimension, quality);
                if (encoded.Length <= ConversationAttachmentLimits.MaximumLength) return new EncodedImage(encoded, JpegMediaType);
            }
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or InvalidDataException)) { throw new InvalidDataException("The image could not be decoded.", exception); }

        throw new InvalidDataException("The image is too large.");
    }

    private static (int Width, int Height) GetScaledSize(int width, int height, int maximumDimension)
    {
        var scale = Math.Min(1, maximumDimension / (double)Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

#if WINDOWS
    private static async Task<byte[]> EncodeJpegAsync(byte[] data, int maximumDimension, int quality)
    {
        using var input = new MemoryStream(data, writable: false).AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(input);
        var (scaledWidth, scaledHeight) = GetScaledSize((int)decoder.PixelWidth, (int)decoder.PixelHeight, maximumDimension);
        var transform = new BitmapTransform { ScaledWidth = (uint)scaledWidth, ScaledHeight = (uint)scaledHeight, InterpolationMode = BitmapInterpolationMode.Fant };
        var pixelData = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        var pixels = pixelData.DetachPixelData();
        FillTransparencyWithWhite(pixels);

        var isRotated = decoder.OrientedPixelWidth != decoder.PixelWidth;
        var outputWidth = isRotated ? scaledHeight : scaledWidth;
        var outputHeight = isRotated ? scaledWidth : scaledHeight;
        using var output = new InMemoryRandomAccessStream();
        var encodingOptions = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(quality / 100f, PropertyType.Single) };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, encodingOptions);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)outputWidth, (uint)outputHeight, 96, 96, pixels);
        await encoder.FlushAsync();

        var encoded = new byte[output.Size];
        using var outputStream = output.GetInputStreamAt(0).AsStreamForRead();
        await outputStream.ReadExactlyAsync(encoded);
        return encoded;
    }

    /// <summary>
    /// Composites premultiplied BGRA pixels over white.
    /// </summary>
    private static void FillTransparencyWithWhite(byte[] pixels)
    {
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var transparency = 255 - pixels[index + 3];
            if (transparency == 0) continue;
            pixels[index] = (byte)(pixels[index] + transparency);
            pixels[index + 1] = (byte)(pixels[index + 1] + transparency);
            pixels[index + 2] = (byte)(pixels[index + 2] + transparency);
            pixels[index + 3] = 255;
        }
    }
#else
    private static Task<byte[]> EncodeJpegAsync(byte[] data, int maximumDimension, int quality) => Task.Run(() =>
    {
        using var codec = SKCodec.Create(SKData.CreateCopy(data)) ?? throw new InvalidDataException("The image format is not supported.");
        using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidDataException("The image could not be decoded.");
        using var image = SKImage.FromBitmap(decoded);
        var (scaledWidth, scaledHeight) = GetScaledSize(decoded.Width, decoded.Height, maximumDimension);
        var origin = codec.EncodedOrigin;
        var isRotated = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var outputWidth = isRotated ? scaledHeight : scaledWidth;
        var outputHeight = isRotated ? scaledWidth : scaledHeight;

        using var output = new SKBitmap(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(SKColors.White);
            canvas.SetMatrix(GetOrientationMatrix(origin, outputWidth, outputHeight));
            canvas.DrawImage(image, new SKRect(0, 0, scaledWidth, scaledHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        using var encoded = output.Encode(SKEncodedImageFormat.Jpeg, quality) ?? throw new InvalidDataException("The image could not be encoded.");
        return encoded.ToArray();
    });

    /// <summary>
    /// The transform that draws the scaled image upright into an output of <paramref name="outputWidth"/> by <paramref name="outputHeight"/> pixels.
    /// </summary>
    private static SKMatrix GetOrientationMatrix(SKEncodedOrigin origin, float outputWidth, float outputHeight) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, outputWidth, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, outputWidth, 0, -1, outputHeight, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, outputHeight, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, outputWidth, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, outputWidth, -1, 0, outputHeight, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, outputHeight, 0, 0, 1),
        _ => SKMatrix.Identity,
    };
#endif
}
