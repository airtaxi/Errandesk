namespace Errandesk.Client.Services;

/// <summary>
/// Turns speech into text with the speech recognition of the platform.
/// </summary>
public interface ISpeechDictationService
{
    /// <summary>
    /// Whether this device can recognize speech.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Listens for speech and returns the recognized text.
    /// </summary>
    /// <param name="prompt">The text shown to the user while the platform listens, where the platform shows one.</param>
    /// <returns>The recognized text, or <see langword="null"/> when the user cancels or nothing is recognized.</returns>
    Task<string?> DictateAsync(string prompt);
}
