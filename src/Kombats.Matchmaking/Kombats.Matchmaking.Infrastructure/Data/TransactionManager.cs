using Kombats.Matchmaking.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kombats.Matchmaking.Infrastructure.Data;

/// <summary>
/// Infrastructure implementation of ITransactionManager using EF Core.
/// </summary>
public class TransactionManager : ITransactionManager
{
    private readonly MatchmakingDbContext _dbContext;

    public TransactionManager(MatchmakingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ITransactionHandle> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransactionHandle(transaction);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private class EfTransactionHandle : ITransactionHandle
    {
        private readonly IDbContextTransaction _transaction;

        public EfTransactionHandle(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.CommitAsync(cancellationToken);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.RollbackAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync();
        }
    }
}


