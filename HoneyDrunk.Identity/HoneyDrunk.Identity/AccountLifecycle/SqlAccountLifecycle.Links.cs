using HoneyDrunk.Identity.Abstractions.Accounts;
using HoneyDrunk.Identity.Abstractions.Authentication;
using HoneyDrunk.Identity.Accounts;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Identity.AccountLifecycle;

/// <summary>Explicit provider linking uses two fresh verified sessions and never email matching.</summary>
public sealed partial class SqlAccountLifecycle
{
    /// <summary>Links an unclaimed verified provider subject to the current active account.</summary>
    /// <param name="current">Fresh proof for the current account.</param>
    /// <param name="additional">Fresh proof for the additional provider identity.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The unchanged canonical account identity.</returns>
    public async Task<AccountStatus> Link(VerifiedLogin current, VerifiedLogin additional, CancellationToken token = default)
    {
        Fresh(current);
        Fresh(additional);
        await using var tx = await Lock(token);
        var user = await Owner(current.Subject, token);
        if (user.State != "Active")
            throw new UnauthorizedAccessException("Recover the account before changing sign-in methods.");
        var key = SqlUserDirectory.SubjectKey(additional.Subject);
        var existing = await db.Subjects.SingleOrDefaultAsync(s => s.SubjectKey == key, token);
        if (existing is not null && existing.UserId != user.UserId)
            throw new InvalidOperationException("This sign-in already belongs to another account; accounts are never merged automatically.");
        if (existing is null)
        {
            if (!await external.Exists(additional.Subject, token))
                throw new UnauthorizedAccessException("The additional provider account is unavailable.");
            db.Subjects.Add(new() { SubjectKey = key, UserId = user.UserId, Issuer = additional.Subject.Issuer, Subject = additional.Subject.Subject, ObjectId = additional.Subject.ObjectId });
            Audit(user, "identity.provider.linked");
            await db.SaveChangesAsync(token);
        }

        await tx.CommitAsync(token);
        return View(user);
    }

    /// <summary>Unlinks only a separately verified owned subject and preserves at least one usable sign-in.</summary>
    /// <param name="current">Fresh current account proof.</param>
    /// <param name="removing">Fresh proof for the method being removed.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The atomic unlink operation.</returns>
    public async Task Unlink(VerifiedLogin current, VerifiedLogin removing, CancellationToken token = default)
    {
        Fresh(current);
        Fresh(removing);
        await using var tx = await Lock(token);
        var user = await Owner(current.Subject, token);
        var removedOwner = await Owner(removing.Subject, token);
        if (user.State != "Active" || user.UserId != removedOwner.UserId)
            throw new UnauthorizedAccessException("The sign-in method is not owned by this active account.");
        var subjects = await db.Subjects.Where(s => s.UserId == user.UserId).ToListAsync(token);
        var key = SqlUserDirectory.SubjectKey(removing.Subject);
        if (subjects.Count <= 1 || SqlUserDirectory.SubjectKey(current.Subject) == key)
            throw new InvalidOperationException("Sign in using a different retained method before removing this one.");
        await external.Revoke(removing.Subject, token);
        db.Subjects.Remove(subjects.Single(s => s.SubjectKey == key));
        Audit(user, "identity.provider.unlinked");
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }
}
