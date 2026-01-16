namespace Kombats.Matchmaking.Application.Abstractions;

/// <summary>
/// Port for managing database transactions.
/// Allows application services to manage transactions without depending on EF Core directly.
/// </summary>
public interface ITransactionManager
{
    /// <summary>
    /// Begins a new transaction and returns a disposable transaction handle.
    /// </summary>
    Task<ITransactionHandle> BeginTransactionAsync(CancellationToken cancellationToken = default);
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


