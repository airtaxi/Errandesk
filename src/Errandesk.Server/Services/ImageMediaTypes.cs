namespace Errandesk.Server.Services;

/// <summary>
/// Recognizes the image formats the assistant accepts from the leading bytes of a file.
/// </summary>
public static class ImageMediaTypes
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Gif = "image/gif";
    public const string Webp = "image/webp";

    /// <summary>The media type of a JPEG, PNG, GIF or WebP image, or <see langword="null"/> for any other content.</summary>
    public static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return Jpeg;
        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return Png;
        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)) return Gif;
        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return Webp;
        return null;
    }
}
