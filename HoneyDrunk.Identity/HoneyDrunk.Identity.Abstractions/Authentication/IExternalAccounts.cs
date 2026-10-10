namespace HoneyDrunk.Identity.Abstractions.Authentication;

/// <summary>Provider credential operations; implementations must fail closed when unavailable.</summary>
public interface IExternalAccounts
{
    /// <summary>Checks that a newly mapped verified subject still has an enabled provider account.</summary>
    /// <param name="subject">Verified subject including its provider object ID.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Whether the provider account exists and is enabled.</returns>
    Task<bool> Exists(ExternalSubject subject, CancellationToken token);

    /// <summary>Revokes provider refresh sessions; application inactivation is independently immediate.</summary>
    /// <param name="subject">Stored verified provider subject.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The provider operation.</returns>
    Task Revoke(ExternalSubject subject, CancellationToken token);

    /// <summary>Idempotently removes both the live and recoverable provider account.</summary>
    /// <param name="subject">Stored verified provider subject.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Completion only after the provider confirms deletion.</returns>
    Task Erase(ExternalSubject subject, CancellationToken token);
}
