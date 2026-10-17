using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Audit.Data;
using HoneyDrunk.Identity.Abstractions.Authentication;
using HoneyDrunk.Identity.Accounts;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Kernel.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Identity.Auditing;

/// <summary>Scopes authentication evidence to erasable accounts; unknown credentials never retain token identifiers.</summary>
public sealed class IdentityValidationAudit(IDbContextFactory<IdentityDbContext> factory) : IAuditLog
{
    /// <inheritdoc />
    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.EventName != "auth.token.validate")
            throw new InvalidOperationException("This adapter accepts only token validation evidence.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='identity:lifecycle',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r < 0 THROW 51000, 'Identity transaction busy.', 1;", cancellationToken);
        string? owner = null;
        var issuer = entry.Metadata?.GetValueOrDefault("claim.iss");
        if (entry.Outcome == AuditOutcome.Succeeded && !string.IsNullOrEmpty(issuer))
        {
            var key = SqlUserDirectory.SubjectKey(new ExternalSubject(issuer, entry.Actor));
            owner = await db.Subjects.Where(s => s.SubjectKey == key).Select(s => s.UserId).SingleOrDefaultAsync(cancellationToken);
        }

        var id = AuditEntryId.New();
        var at = owner is null ? new DateTimeOffset(entry.OccurredAt.UtcDateTime.Date.AddHours(entry.OccurredAt.UtcDateTime.Hour), TimeSpan.Zero) : entry.OccurredAt;

        // No subject, JWT ID, claims, request correlation, device/IP or precise event
        // time survives for unknown/erased accounts. Only hourly outcome evidence remains.
        db.Audit.Add(AuditRecord.FromEntry(new AuditEntry(id, at, owner ?? "anonymous", entry.EventName, AuditCategory.Security, entry.Outcome, new AuditTarget(owner is null ? "identity.validation" : "user", owner ?? "anonymous"), TenantId.Internal, owner is null ? id.ToString() : entry.CorrelationId)));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
