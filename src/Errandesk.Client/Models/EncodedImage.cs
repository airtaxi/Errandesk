namespace Errandesk.Client.Models;

/// <summary>
/// An image file ready to upload.
/// </summary>
/// <param name="Data">The encoded image.</param>
/// <param name="MediaType">The media type of <paramref name="Data"/>.</param>
public sealed record EncodedImage(byte[] Data, string MediaType);
