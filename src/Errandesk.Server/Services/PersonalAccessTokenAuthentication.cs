using Errandesk.Server.Data;

namespace Errandesk.Server.Services;

/// <summary>
/// A valid personal access token and the user it belongs to.
/// </summary>
public sealed record PersonalAccessTokenAuthentication(Guid TokenId, UserDocument User);
