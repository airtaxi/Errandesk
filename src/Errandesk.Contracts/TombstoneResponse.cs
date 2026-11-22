namespace Errandesk.Contracts;

/// <summary>
/// A deleted list, task or note.
/// </summary>
/// <param name="Id">ID of the deleted item.</param>
/// <param name="ChangeVersion">Global change version of the deletion.</param>
/// <param name="DeletedAt">When the item was deleted.</param>
public sealed record TombstoneResponse(Guid Id, long ChangeVersion, DateTimeOffset DeletedAt);
