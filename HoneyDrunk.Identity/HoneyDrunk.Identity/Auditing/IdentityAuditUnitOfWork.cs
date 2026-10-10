using HoneyDrunk.Audit.Data;
using HoneyDrunk.Data.Abstractions.Repositories;
using HoneyDrunk.Data.Abstractions.Transactions;
using HoneyDrunk.Data.EntityFramework.Transactions;
using HoneyDrunk.Identity.Persistence.Context;

namespace HoneyDrunk.Identity.Auditing;

/// <summary>Maps the Audit persistence marker to Identity's owned SQL context.</summary>
public sealed class IdentityAuditUnitOfWork(IdentityDbContext context) : IUnitOfWork<IAuditDataContext>
{
    private readonly EfUnitOfWork<IdentityDbContext> inner = new(context);

    /// <inheritdoc />
    public bool HasPendingChanges => inner.HasPendingChanges;

    /// <inheritdoc />
    public IRepository<TEntity> Repository<TEntity>()
        where TEntity : class
        => inner.Repository<TEntity>();

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default) => inner.BeginTransactionAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
