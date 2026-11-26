using System.Text;
using System.Text.RegularExpressions;

namespace Errandesk.Server.Web;

/// <summary>
/// Turns the bytes of a web page into text, choosing the encoding from the byte order mark, the Content-Type charset, or an HTML meta tag, in that order, and UTF-8 otherwise.
/// </summary>
public static partial class WebPageTextDecoder
{
    private const int MetaCharsetSearchByteCount = 4096;

    static WebPageTextDecoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Decode(ReadOnlySpan<byte> bytes, string? charset, bool isHtml)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return Encoding.UTF8.GetString(bytes[3..]);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])) return Encoding.Unicode.GetString(bytes[2..]);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) return Encoding.BigEndianUnicode.GetString(bytes[2..]);

        var encoding = FindEncoding(charset) ?? (isHtml ? FindEncoding(FindMetaCharset(bytes)) : null) ?? Encoding.UTF8;
        return encoding.GetString(bytes);
    }

    private static string? FindMetaCharset(ReadOnlySpan<byte> bytes)
    {
        var head = Encoding.Latin1.GetString(bytes[..Math.Min(bytes.Length, MetaCharsetSearchByteCount)]);
        var match = MetaCharsetRegex().Match(head);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static Encoding? FindEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return Encoding.GetEncoding(name.Trim().Trim('"', '\'')); }
        catch (ArgumentException) { return null; }
    }

    [GeneratedRegex("""<meta[^>]+charset\s*=\s*["']?\s*([A-Za-z0-9_.:\-]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharsetRegex();
}
