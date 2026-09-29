using HoneyDrunk.Audit.Abstractions;
using HoneyDrunk.Audit.Data;
using HoneyDrunk.Identity.Abstractions.Accounts;
using HoneyDrunk.Identity.Abstractions.Authentication;
using HoneyDrunk.Identity.Persistence.Context;
using HoneyDrunk.Identity.Persistence.Entities;
using HoneyDrunk.Kernel.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HoneyDrunk.Identity.Accounts;

/// <summary>Creates stable user mappings under a SQL transaction and account-specific lock.</summary>
public sealed class SqlUserDirectory(IdentityDbContext db, TimeProvider clock, IExternalAccounts external) : IUserDirectory
{
    /// <inheritdoc />
    public async Task<UserRecord> Resolve(ExternalSubject verifiedSubject, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedSubject.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedSubject.Subject);
        var key = SubjectKey(verifiedSubject);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var resource = "identity:lifecycle";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @result < 0 THROW 51000, 'Identity transaction busy.', 1;", cancellationToken);
        var mapping = await db.Subjects.SingleOrDefaultAsync(s => s.SubjectKey == key, cancellationToken);
        UserEntity user;
        if (mapping is null)
        {
            if (!await external.Exists(verifiedSubject, cancellationToken))
                throw new UnauthorizedAccessException("Provider account no longer exists or is disabled.");
            user = new() { UserId = "usr_" + Ulid.NewUlid(), CreatedAt = clock.GetUtcNow() };
            db.Users.Add(user);
            db.Subjects.Add(new() { SubjectKey = key, UserId = user.UserId, Issuer = verifiedSubject.Issuer, Subject = verifiedSubject.Subject, ObjectId = verifiedSubject.ObjectId });
            db.Audit.Add(AuditRecord.FromEntry(new AuditEntry(AuditEntryId.New(), clock.GetUtcNow(), user.UserId, "identity.user.created", AuditCategory.DataChange, AuditOutcome.Succeeded, new AuditTarget("user", user.UserId), TenantId.Internal, Activity.Current?.TraceId.ToString(), AuditOperation.Create)));
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (mapping.ObjectId is not null && verifiedSubject.ObjectId != mapping.ObjectId)
                throw new UnauthorizedAccessException("Provider account binding changed.");
            mapping.Issuer = verifiedSubject.Issuer;
            mapping.Subject = verifiedSubject.Subject;
            mapping.ObjectId ??= verifiedSubject.ObjectId;
            await db.SaveChangesAsync(cancellationToken);
            user = await db.Users.SingleAsync(u => u.UserId == mapping.UserId, cancellationToken);
        }

        if (user.State != "Active")
            throw new UnauthorizedAccessException("Account is not active.");
        await tx.CommitAsync(cancellationToken);
        return new(user.UserId, user.State, user.CreatedAt, user.Version, user.DeletionPausedAt);
    }

    internal static string SubjectKey(ExternalSubject subject) => Hash(JsonSerializer.Serialize(new { subject.Issuer, subject.Subject }));

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
