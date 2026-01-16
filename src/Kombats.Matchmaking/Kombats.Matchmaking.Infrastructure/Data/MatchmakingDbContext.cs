using Kombats.Matchmaking.Infrastructure.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Matchmaking.Infrastructure.Data;

/// <summary>
/// EF Core DbContext for Matchmaking service.
/// </summary>
public class MatchmakingDbContext : DbContext
{
    public MatchmakingDbContext(DbContextOptions<MatchmakingDbContext> options)
        : base(options)
    {
    }

    public DbSet<MatchEntity> Matches => Set<MatchEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure MatchEntity
        modelBuilder.Entity<MatchEntity>(entity =>
        {
            entity.ToTable("matches");

            entity.HasKey(e => e.MatchId);

            entity.Property(e => e.MatchId)
                .IsRequired();

            entity.Property(e => e.BattleId)
                .IsRequired();

            entity.Property(e => e.PlayerAId)
                .IsRequired();

            entity.Property(e => e.PlayerBId)
                .IsRequired();

            entity.Property(e => e.Variant)
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(e => e.State)
                .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                .IsRequired();

            entity.Property(e => e.UpdatedAtUtc)
                .IsRequired();

            // Unique index on BattleId
            entity.HasIndex(e => e.BattleId)
                .IsUnique();

            // Index on PlayerAId
            entity.HasIndex(e => e.PlayerAId);

            // Index on PlayerBId
            entity.HasIndex(e => e.PlayerBId);
        });

        // Configure MassTransit EF Core integration entities (Inbox/Outbox)
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}




