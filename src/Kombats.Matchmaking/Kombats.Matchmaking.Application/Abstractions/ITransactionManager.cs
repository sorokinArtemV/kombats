namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for managing database transactions and unit of work.
/// Allows application services to manage transactions without depending on EF Core directly.
/// </summary>
public interface ITransactionManager
{
    /// <summary>
    /// Begins a new transaction and returns a disposable transaction handle.
    /// </summary>
    Task<ITransactionHandle> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves all pending changes to the database within the current transaction.
    /// This should be called once per transaction after all entities are added/modified.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Handle for a database transaction.
/// </summary>
public interface ITransactionHandle : IAsyncDisposable
{
    /// <summary>
    /// Commits the transaction.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}


