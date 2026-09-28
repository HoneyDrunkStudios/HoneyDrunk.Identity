using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Audit.Data;
using HoneyDrunk.Identity.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HoneyDrunk.Identity;

/// <summary>Creates stable user mappings under a SQL transaction and account-specific lock.</summary>
public sealed class SqlUserDirectory(IdentityDbContext db, TimeProvider clock) : IUserDirectory
{
    /// <inheritdoc />
    public async Task<UserRecord> Resolve(ExternalSubject verifiedSubject, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedSubject.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedSubject.Subject);
        var key = Hash(JsonSerializer.Serialize(verifiedSubject));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var resource = "identity:" + key;
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @result < 0 THROW 51000, 'Identity transaction busy.', 1;", cancellationToken);
        var mapping = await db.Subjects.SingleOrDefaultAsync(s => s.SubjectKey == key, cancellationToken);
        UserRow user;
        if (mapping is null)
        {
            user = new() { UserId = "usr_" + Ulid.NewUlid(), CreatedAt = clock.GetUtcNow() };
            db.Users.Add(user);
            db.Subjects.Add(new() { SubjectKey = key, UserId = user.UserId });
            db.Audit.Add(AuditRecord.FromEntry(new AuditEntry(AuditEntryId.New(), clock.GetUtcNow(), user.UserId, "identity.user.created", AuditCategory.DataChange, AuditOutcome.Succeeded, new AuditTarget("user", user.UserId), TenantId.Internal, Activity.Current?.TraceId.ToString(), AuditOperation.Create)));
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            user = await db.Users.SingleAsync(u => u.UserId == mapping.UserId, cancellationToken);
        }

        if (user.State != "Active")
            throw new UnauthorizedAccessException("Account is not active.");
        await tx.CommitAsync(cancellationToken);
        return new(user.UserId, user.State, user.CreatedAt);
    }

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
