#if ANDROID || IOS || WINDOWS
using Windows.Security.Credentials;
#else
using Windows.Storage;
#endif

namespace Errandesk.Client.Services;

/// <summary>
/// Keeps the refresh token of the signed-in session.
/// Windows, Android and iOS use the platform credential vault (Credential Locker, AndroidKeyStore, Keychain).
/// The desktop target, which has no credential vault, uses a file in the application's local folder that only the current user can read.
/// </summary>
public sealed class RefreshTokenStore
{
#if ANDROID || IOS || WINDOWS
    private const string ResourceName = "Errandesk";
    private const string AccountName = "Session";

    private readonly PasswordVault _passwordVault = new();

    public string? Read()
    {
        try
        {
            var credential = _passwordVault.Retrieve(ResourceName, AccountName);
            credential.RetrievePassword();
            return string.IsNullOrEmpty(credential.Password) ? null : credential.Password;
        }
        catch (Exception) { return null; }
    }

    public void Write(string refreshToken)
    {
        Clear();
        _passwordVault.Add(new PasswordCredential(ResourceName, AccountName, refreshToken));
    }

    public void Clear()
    {
        try { _passwordVault.Remove(_passwordVault.Retrieve(ResourceName, AccountName)); }
        catch (Exception) { }
    }
#else
    private const string TokenFileName = "session-token";

    private static string TokenFilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, TokenFileName);

    public string? Read()
    {
        try
        {
            var refreshToken = File.ReadAllText(TokenFilePath).Trim();
            return refreshToken.Length == 0 ? null : refreshToken;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Write(string refreshToken)
    {
        var fileStreamOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) fileStreamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using var writer = new StreamWriter(TokenFilePath, fileStreamOptions);
        writer.Write(refreshToken);
    }

    public void Clear()
    {
        try { File.Delete(TokenFilePath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
#endif
}
