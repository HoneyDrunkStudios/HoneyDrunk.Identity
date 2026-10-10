using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Identity.Accounts;
using HoneyDrunk.Identity.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Identity.AccountLifecycle;

/// <summary>Retryable worker operations; no public endpoint accepts arbitrary account IDs for erasure.</summary>
public sealed partial class SqlAccountLifecycle
{
    /// <summary>Advances one existing request under the same lock as recovery and linking.</summary>
    /// <param name="userId">An account selected by the trusted worker.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Completion of one bounded attempt.</returns>
    public async Task Process(string userId, CancellationToken token = default)
    {
        await using var tx = await Lock(token);
        var user = await db.Users.SingleOrDefaultAsync(u => u.UserId == userId, token);
        if (user is null || user.NextAttemptAt > clock.GetUtcNow())
            return;
        if (user.State == "Inactive" && clock.GetUtcNow() >= user.RecoveryDeadline)
            await Transition(user, "Erasing", token);
        var deliveries = await db.Deliveries.Where(d => d.UserId == userId).ToListAsync(token);
        foreach (var delivery in deliveries.Where(d => !d.Acknowledged && d.ExpiresAt <= clock.GetUtcNow()))
            Queue(user, delivery);
        try
        {
            var subjects = await db.Subjects.Where(s => s.UserId == userId).ToListAsync(token);
            if (user.State != "Active" && !user.SessionsRevoked)
            {
                foreach (var subject in subjects)
                    await external.Revoke(new(subject.Issuer, subject.Subject, subject.ObjectId), token);
                user.SessionsRevoked = true;
            }

            if (user.State == "Erasing" && deliveries.Count > 0 && deliveries.All(d => d.Acknowledged && d.Version == user.Version))
            {
                foreach (var subject in subjects)
                    await external.Erase(new(subject.Issuer, subject.Subject, subject.ObjectId), token);
                await Purge(userId, subjects, token);
                db.Erasures.Add(new() { UserId = userId, ErasedAt = clock.GetUtcNow() });
            }
            else
            {
                user.NextAttemptAt = clock.GetUtcNow().AddMinutes(1);
                user.FailureCode = user.State == "Erasing" ? "AwaitingConsumerAcknowledgment" : null;
            }
        }
        catch (Exception error) when (error is HttpRequestException or UnauthorizedAccessException)
        {
            user.FailureCode = "ProviderLifecycleUnavailable";
            user.NextAttemptAt = clock.GetUtcNow().AddMinutes(1);
        }

        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }

    /// <summary>Reapplies an authoritative current marker before a restored database can serve traffic.</summary>
    /// <param name="marker">Marker fetched from the recovery ledger outside the restored backup.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The atomic purge; no new provider calls or audit events are created.</returns>
    public async Task ReapplyErasure(ErasureMarkerEntity marker, CancellationToken token = default)
    {
        if (marker.ErasedAt > clock.GetUtcNow() || marker.ErasedAt <= clock.GetUtcNow().AddDays(-35))
            throw new ArgumentException("Restore requires a current verified erasure marker.");
        await using var tx = await Lock(token);
        var subjects = await db.Subjects.Where(s => s.UserId == marker.UserId).ToListAsync(token);
        await Purge(marker.UserId, subjects, token);
        var existing = await db.Erasures.SingleOrDefaultAsync(m => m.UserId == marker.UserId, token);
        if (existing is null)
            db.Erasures.Add(new() { UserId = marker.UserId, ErasedAt = marker.ErasedAt });
        else
            existing.ErasedAt = marker.ErasedAt;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }

    /// <summary>Removes expired minimal markers only after their full post-erasure retention period.</summary>
    /// <param name="token">Cancellation.</param>
    /// <returns>The bounded retention cleanup.</returns>
    public async Task Prune(CancellationToken token = default)
    {
        await using var tx = await Lock(token);
        var cutoff = clock.GetUtcNow().AddDays(-35);
        await db.Erasures.Where(m => m.ErasedAt <= cutoff).ExecuteDeleteAsync(token);
        var deliveredBefore = clock.GetUtcNow().AddHours(-1);
        await db.Set<OutboxMessage>().Where(m => m.Type == typeof(HoneyDrunk.Identity.Abstractions.AccountLifecycle.LifecycleIntent).AssemblyQualifiedName && m.OccurredAt <= deliveredBefore).ExecuteDeleteAsync(token);
        var logCutoff = clock.GetUtcNow().AddDays(-30);
        await db.Audit.Where(a => a.Actor == "anonymous" && a.EventName == "auth.token.validate" && a.OccurredAt <= logCutoff).ExecuteDeleteAsync(token);
        await tx.CommitAsync(token);
    }

    private static bool BelongsToErasedUser(object entity, string userId, string ownerProperty) => entity switch
    {
        UserEntity user => user.UserId == userId,
        SubjectEntity subject => subject.UserId == userId,
        LifecycleDeliveryEntity delivery => delivery.UserId == userId,
        OutboxMessage message => message.Payload.Contains(ownerProperty, StringComparison.Ordinal),
        _ => false,
    };

    private async Task Purge(string userId, List<SubjectEntity> subjects, CancellationToken token)
    {
        var rawSubjects = subjects.Select(s => s.Subject).Where(s => !string.IsNullOrEmpty(s)).ToArray();
        var digests = subjects.Select(s => s.SubjectKey).Append(SqlUserDirectory.Hash(userId)).ToArray();

        // Scoped exception to append-only writes: user data and historical validation
        // records are erased together; unrelated accounts and anonymous aggregates remain.
        await db.Audit.Where(a => a.Actor == userId || a.TargetId == userId).ExecuteDeleteAsync(token);
        var legacyValidation = await db.Audit.Where(a => a.EventName == "auth.token.validate" && rawSubjects.Contains(a.Actor)).ToListAsync(token);
        foreach (var record in legacyValidation)
        {
            var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(record.MetadataJson);
            if (subjects.Any(s => s.Subject == record.Actor && metadata?.GetValueOrDefault("claim.iss") == s.Issuer))
                db.Audit.Remove(record);
        }

        await db.LegacyAudit.Where(a => digests.Contains(a.ActorHash)).ExecuteDeleteAsync(token);
        var ownerProperty = "\"UserId\":" + System.Text.Json.JsonSerializer.Serialize(userId);
        await db.Set<OutboxMessage>().Where(m => m.Type == typeof(HoneyDrunk.Identity.Abstractions.AccountLifecycle.LifecycleIntent).AssemblyQualifiedName && m.Payload.Contains(ownerProperty)).ExecuteDeleteAsync(token);
        await db.Deliveries.Where(d => d.UserId == userId).ExecuteDeleteAsync(token);
        await db.Subjects.Where(s => s.UserId == userId).ExecuteDeleteAsync(token);
        await db.Users.Where(u => u.UserId == userId).ExecuteDeleteAsync(token);
        foreach (var entry in db.ChangeTracker.Entries().Where(e => BelongsToErasedUser(e.Entity, userId, ownerProperty)).ToArray())
            entry.State = EntityState.Detached;
    }
}
