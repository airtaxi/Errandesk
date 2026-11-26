using Errandesk.Client.Droid;

namespace Errandesk.Client.Services;

/// <summary>
/// Speech dictation on Android through the speech recognition screen of the system.
/// </summary>
public sealed class AndroidSpeechDictationService : ISpeechDictationService
{
    public bool IsAvailable => MainActivity.IsSpeechRecognitionAvailable;

    public Task<string?> DictateAsync(string prompt) => MainActivity.RecognizeSpeechAsync(prompt);
}
