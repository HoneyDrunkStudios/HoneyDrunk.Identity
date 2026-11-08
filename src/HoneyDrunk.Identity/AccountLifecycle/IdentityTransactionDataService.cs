using HoneyDrunk.Identity.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HoneyDrunk.Identity.AccountLifecycle;

/// <summary>Serializes account mutations without making one provider request block every account.</summary>
/// <param name="db">The transaction's scoped database context.</param>
internal sealed class IdentityTransactionDataService(IdentityDbContext db)
{
    internal Task<IDbContextTransaction> ForUser(string userId, CancellationToken token) =>
        Begin([], userId, false, token);

    internal Task<IDbContextTransaction> ForSubjects(string[] keys, CancellationToken token) =>
        Begin(keys, null, false, token);

    internal Task<IDbContextTransaction> ForMaintenance(CancellationToken token) =>
        Begin([], null, true, token);

    private async Task<IDbContextTransaction> Begin(string[] keys, string? userId, bool maintenance, CancellationToken token)
    {
        var tx = await db.Database.BeginTransactionAsync(token);
        try
        {
            // Maintenance and older hosts retain the exclusive gate. Ordinary operations
            // share it, then take subject locks before account locks in stable order.
            await Acquire("identity:lifecycle", maintenance ? "Exclusive" : "Shared", token);
            foreach (var key in keys.Distinct().Order(StringComparer.Ordinal))
                await Acquire("identity:subject:" + key, "Exclusive", token);
            var owners = keys.Length == 0
                ? []
                : await db.Subjects.AsNoTracking().Where(s => keys.Contains(s.SubjectKey)).Select(s => s.UserId).ToArrayAsync(token);
            foreach (var owner in owners.Concat(userId is null ? [] : new[] { userId }).Distinct().Order(StringComparer.Ordinal))
                await Acquire("identity:user:" + owner.TrimEnd(' ').ToUpperInvariant(), "Exclusive", token);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private Task Acquire(string resource, string mode, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode={mode},@LockOwner='Transaction',@LockTimeout=10000; IF @r < 0 THROW 51000, 'Identity transaction busy.', 1;", token);
}
