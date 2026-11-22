using Errandesk.Contracts;

namespace Errandesk.Server.Tests.Infrastructure;

/// <summary>
/// A registered user with an API client that sends the user's current access token.
/// </summary>
public sealed class TestUser(AuthenticationResponse authentication, ApiClient client)
{
    public AuthenticationResponse Authentication { get; private set; } = authentication;

    public ApiClient Client => client;

    public Guid Id => Authentication.User.Id;

    public string RefreshToken => Authentication.RefreshToken;

    /// <summary>Switches to the tokens of a newer authentication response of the same session.</summary>
    public void Update(AuthenticationResponse newAuthentication)
    {
        Authentication = newAuthentication;
        client.AccessToken = newAuthentication.AccessToken;
    }
}
