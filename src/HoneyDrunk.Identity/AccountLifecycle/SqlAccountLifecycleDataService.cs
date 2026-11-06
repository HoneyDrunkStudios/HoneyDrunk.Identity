using HoneyDrunk.Identity.Abstractions.AccountLifecycle;
using HoneyDrunk.Identity.Abstractions.Accounts;
using HoneyDrunk.Identity.Abstractions.Authentication;
using HoneyDrunk.Identity.Accounts;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Identity.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace HoneyDrunk.Identity.AccountLifecycle;

/// <summary>Serializes verified account lifecycle transitions and their transactional delivery intents.</summary>
public sealed partial class SqlAccountLifecycleDataService(IdentityDbContext db, TimeProvider clock, IExternalAccounts external, IOptions<LifecycleOptions> options)
{
    private readonly AccountLifecycleStateDataService state = new(db, clock, options);

    /// <summary>Reads an existing account without restoring ordinary access.</summary>
    /// <param name="subject">Validated owner identity.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The account status.</returns>
    public async Task<AccountStatus> Status(ExternalSubject subject, CancellationToken token = default)
    {
        await using var tx = await state.Lock(token);
        return View(await Owner(subject, token));
    }

    /// <summary>Inactivates the owner immediately; repeats preserve the original recovery deadline.</summary>
    /// <param name="login">Recent verified authentication.</param>
    /// <param name="confirmed">Explicit deletion confirmation.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The durable deletion status.</returns>
    public async Task<AccountStatus> Request(VerifiedLogin login, bool confirmed, CancellationToken token = default)
    {
        Fresh(login);
        if (!confirmed)
            throw new ArgumentException("Explicit deletion confirmation is required.");
        await using var tx = await state.Lock(token);
        var user = await Owner(login.Subject, token);
        if (user.State == "Active")
        {
            if (!options.Value.DeliveryEnabled || options.Value.Consumers.Count == 0)
                throw new InvalidOperationException("Deletion consumer registry is not configured.");
            user.RequestedAt = clock.GetUtcNow();
            user.DeletionPausedAt = user.RequestedAt;
            user.RecoveryDeadline = user.RequestedAt.Value.AddDays(30);
            user.SessionsRevoked = false;
            await state.Transition(user, "Inactive", token);
            state.Audit(user, "identity.deletion.requested");
            await db.SaveChangesAsync(token);
        }

        await tx.CommitAsync(token);
        return View(user);
    }

    /// <summary>Explicitly cancels strictly before the original deadline; downstream commitments remain paused.</summary>
    /// <param name="login">Recent same-account authentication.</param>
    /// <param name="confirmed">Explicit recovery confirmation.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The restored account status.</returns>
    public async Task<AccountStatus> Cancel(VerifiedLogin login, bool confirmed, CancellationToken token = default)
    {
        Fresh(login);
        if (!confirmed)
            throw new ArgumentException("Explicit recovery confirmation is required.");
        await using var tx = await state.Lock(token);
        var user = await Owner(login.Subject, token);
        if (user.State != "Active")
        {
            if (user.State != "Inactive" || clock.GetUtcNow() >= user.RecoveryDeadline)
                throw new InvalidOperationException("The recovery window has ended; erasure continues.");
            await state.Transition(user, "Active", token);
            user.RequestedAt = null;
            user.RecoveryDeadline = null;
            state.Audit(user, "identity.deletion.cancelled");
            await db.SaveChangesAsync(token);
        }

        await tx.CommitAsync(token);
        return View(user);
    }

    /// <summary>Accepts only the current registered consumer's capability after its committed mutation.</summary>
    /// <param name="ack">Receipt received on Identity's private Transport endpoint.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The durable receipt write.</returns>
    public async Task Acknowledge(LifecycleAck ack, CancellationToken token = default)
    {
        await using var tx = await state.Lock(token);
        var row = await db.Deliveries.SingleOrDefaultAsync(d => d.UserId == ack.UserId && d.Consumer == ack.Consumer, token);
        if (row is null || row.Version != ack.Version || row.ExpiresAt <= clock.GetUtcNow())
            return;
        if (ack.Acknowledgment.Length != 64 || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(row.Acknowledgment), System.Text.Encoding.ASCII.GetBytes(ack.Acknowledgment)))
            throw new UnauthorizedAccessException("Invalid lifecycle acknowledgment.");
        row.Acknowledged = true;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }

    private static AccountStatus View(UserEntity user) => new(user.UserId, user.State, user.RequestedAt, user.RecoveryDeadline, user.Version);

    private async Task<UserEntity> Owner(ExternalSubject subject, CancellationToken token)
    {
        var key = SqlUserDirectoryDataService.SubjectKey(subject);
        var mapping = await db.Subjects.SingleOrDefaultAsync(s => s.SubjectKey == key, token)
            ?? throw new UnauthorizedAccessException("Account does not exist.");
        if (mapping.ObjectId is not null && mapping.ObjectId != subject.ObjectId)
            throw new UnauthorizedAccessException("Provider account binding changed.");
        return await db.Users.SingleAsync(u => u.UserId == mapping.UserId, token);
    }

    private void Fresh(VerifiedLogin login)
    {
        var now = clock.GetUtcNow();
        if (login.AuthenticatedAt is null || login.AuthenticatedAt > now.AddSeconds(30) || login.AuthenticatedAt <= now.AddMinutes(-5))
            throw new UnauthorizedAccessException("Sign in again to confirm this account change.");
    }
}
