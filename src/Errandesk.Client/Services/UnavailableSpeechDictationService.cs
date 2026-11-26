namespace Errandesk.Client.Services;

/// <summary>
/// Speech dictation on platforms without speech recognition support in the app.
/// </summary>
public sealed class UnavailableSpeechDictationService : ISpeechDictationService
{
    public bool IsAvailable => false;

    public Task<string?> DictateAsync(string prompt) => Task.FromResult<string?>(null);
}
