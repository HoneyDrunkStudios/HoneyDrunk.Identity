using HoneyDrunk.Identity.Abstractions.Authentication;

namespace HoneyDrunk.Identity.Abstractions.Accounts;

/// <summary>Resolves verified external identities to stable HoneyDrunk accounts.</summary>
public interface IUserDirectory
{
    /// <summary>Returns an active account, atomically creating its first provider mapping when needed.</summary>
    /// <param name="verifiedSubject">Subject validated by the authentication boundary.</param>
    /// <param name="cancellationToken">Cancellation for the database operation.</param>
    /// <returns>The active account; inactive accounts are rejected.</returns>
    Task<UserRecord> Resolve(ExternalSubject verifiedSubject, CancellationToken cancellationToken = default);
}
